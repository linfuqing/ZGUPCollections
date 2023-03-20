using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Mathematics;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Experimental.AI;

namespace ZG
{
    [System.Flags]
    public enum NavMeshWayPointFlag
    {
        Start = 0x01,              // The vertex is the start position.
        End = 0x02,                // The vertex is the end position.
        OffMeshConnection = 0x04   // The vertex is start of an off-mesh link.
    }

    public struct NavMeshWayPoint
    {
        public NavMeshWayPointFlag flag;
        public NavMeshLocation location;

        public NavMeshWayPoint(NavMeshWayPointFlag flag, NavMeshLocation location)
        {
            this.flag = flag;
            this.location = location;
        }
    }
    
    [NativeContainer]
    public struct NavMeshQueryWrapper
    {
        private Allocator __allocator;

        [NativeSetThreadIndex]
        private int __threadIndex;

        [NativeDisableUnsafePtrRestriction]
        private unsafe void* __values;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
        internal AtomicSafetyHandle m_Safety;

        [NativeSetClassTypeToNullOnSchedule]
        private DisposeSentinel __disposeSentinel;

        private GCHandle __arrayHandle;
#endif

        public unsafe NavMeshQueryWrapper(NavMeshWorld world, Allocator allocator, int pathNodePoolSize = 0)
        {
            __allocator = allocator;

            __threadIndex = 0;

            int threadCount = Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount;

            __values = UnsafeUtility.Malloc(
                           UnsafeUtility.SizeOf<NavMeshQuery>() * threadCount,
                           UnsafeUtility.AlignOf<NavMeshQuery>(),
                           allocator);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            var navMeshQueries = new NavMeshQuery[threadCount];
#endif

            NavMeshQuery navMeshQuery;
            for (int i = 0; i < threadCount; ++i)
            {
                navMeshQuery = new NavMeshQuery(world, allocator, pathNodePoolSize);

                UnsafeUtility.WriteArrayElement(__values, i, navMeshQuery);

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                navMeshQueries[i] = navMeshQuery;
#endif
            }

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Create(out m_Safety, out __disposeSentinel, 0, allocator);

            __arrayHandle = GCHandle.Alloc(navMeshQueries);
#endif
        }

        public unsafe void Dispose()
        {
            int threadCount = Unity.Jobs.LowLevel.Unsafe.JobsUtility.MaxJobThreadCount;
            for (int i = 0; i < threadCount; ++i)
                UnsafeUtility.ReadArrayElement<NavMeshQuery>(__values, i).Dispose();

            UnsafeUtility.Free(__values, __allocator);

            __values = null;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            DisposeSentinel.Dispose(ref m_Safety, ref __disposeSentinel);

            __arrayHandle.Free();
#endif

        }

        public unsafe static implicit operator NavMeshQuery(in NavMeshQueryWrapper value)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(value.m_Safety);
#endif
            return UnsafeUtility.ReadArrayElement<NavMeshQuery>(value.__values, value.__threadIndex);
        }
    }
    
    public static class NavMeshQueryUtility
    {
        // Calculate the closest point of approach for line-segment vs line-segment.
        public static bool SegmentSegmentCPA(out float3 c0, out float3 c1, float3 p0, float3 p1, float3 q0, float3 q1)
        {
            var u = p1 - p0;
            var v = q1 - q0;
            var w0 = p0 - q0;

            float a = math.dot(u, u);
            float b = math.dot(u, v);
            float c = math.dot(v, v);
            float d = math.dot(u, w0);
            float e = math.dot(v, w0);

            float den = (a * c - b * b);
            float sc, tc;

            if (den == 0)
            {
                sc = 0;
                tc = d / b;

                // todo: handle b = 0 (=> a and/or c is 0)
            }
            else
            {
                sc = (b * e - c * d) / (a * c - b * b);
                tc = (a * e - b * d) / (a * c - b * b);
            }

            c0 = math.lerp(p0, p1, sc);
            c1 = math.lerp(q0, q1, tc);

            return den != 0;
        }

        public static float Perp2D(Vector3 u, Vector3 v)
        {
            return u.z * v.x - u.x * v.z;
        }

        public static void Swap(ref Vector3 a, ref Vector3 b)
        {
            var temp = a;
            a = b;
            b = temp;
        }

        // Retrace portals between corners and register if type of polygon changes
        public static void RetracePortals(
            this in NavMeshQuery query, 
            int startIndex, 
            int endIndex, 
            in Vector3 termPos,
            in NativeSlice<PolygonId> polygons,
            ref DynamicBuffer<NavMeshWayPoint> wayPoints)
        {
#if DEBUG_CROWDSYSTEM_ASSERTS
        Assert.IsTrue(n < maxStraightPath);
        Assert.IsTrue(startIndex <= endIndex);
#endif

            Vector3 l, r;
            float3 cpa1, cpa2;
            for (var k = startIndex; k < endIndex - 1; ++k)
            {
                var type1 = query.GetPolygonType(polygons[k]);
                var type2 = query.GetPolygonType(polygons[k + 1]);
                if (type1 != type2)
                {
                    var result = query.GetPortalPoints(polygons[k], polygons[k + 1], out l, out r);
                    
                    UnityEngine.Assertions.Assert.IsTrue(result); // Expect path elements k, k+1 to be verified

                    SegmentSegmentCPA(out cpa1, out cpa2, l, r, wayPoints[wayPoints.Length - 1].location.position, termPos);
                    wayPoints.Add(new NavMeshWayPoint(
                        (type2 == NavMeshPolyTypes.OffMeshConnection) ? NavMeshWayPointFlag.OffMeshConnection : 0, 
                        query.CreateLocation(cpa1, polygons[k + 1])));
                }
            }
            wayPoints.Add(new NavMeshWayPoint(
                query.GetPolygonType(polygons[endIndex]) == NavMeshPolyTypes.OffMeshConnection ? NavMeshWayPointFlag.OffMeshConnection : 0,
                query.CreateLocation(termPos, polygons[endIndex])));
        }

        public static PathQueryStatus FindStraightPath(
            this in NavMeshQuery query, 
            in Vector3 startPos, 
            in Vector3 endPos, 
            in NativeSlice<PolygonId> polygons, 
            ref DynamicBuffer<NavMeshWayPoint> wayPoints, 
            ref DynamicBuffer<float> vertexSides)
        {
            if (!query.IsValid(polygons[0]))
                return PathQueryStatus.Failure | PathQueryStatus.InvalidParam;

            wayPoints.Add(new NavMeshWayPoint(NavMeshWayPointFlag.Start, query.CreateLocation(startPos, polygons[0])));
            
            var apexIndex = 0;

            int pathSize = polygons.Length;
            if (pathSize > 1)
            {
                var startPolyWorldToLocal = query.PolygonWorldToLocalMatrix(polygons[0]);

                var apex = startPolyWorldToLocal.MultiplyPoint(startPos);
                var left = new Vector3(0, 0, 0); // Vector3.zero accesses a static readonly which does not work in burst yet
                var right = new Vector3(0, 0, 0);
                var leftIndex = -1;
                var rightIndex = -1;

                Vector3 vl, vr;
                for (var i = 1; i <= pathSize; ++i)
                {
                    var polyWorldToLocal = query.PolygonWorldToLocalMatrix(polygons[apexIndex]);

                    if (i == pathSize)
                        vl = vr = polyWorldToLocal.MultiplyPoint(endPos);
                    else
                    {
                        var success = query.GetPortalPoints(polygons[i - 1], polygons[i], out vl, out vr);
                        if (!success)
                            return PathQueryStatus.Failure | PathQueryStatus.InvalidParam;

#if DEBUG_CROWDSYSTEM_ASSERTS
                    Assert.IsTrue(query.IsValid(path[i - 1]));
                    Assert.IsTrue(query.IsValid(path[i]));
#endif

                        vl = polyWorldToLocal.MultiplyPoint(vl);
                        vr = polyWorldToLocal.MultiplyPoint(vr);
                    }

                    vl = vl - apex;
                    vr = vr - apex;

                    // Ensure left/right ordering
                    if (Perp2D(vl, vr) < 0)
                        Swap(ref vl, ref vr);

                    // Terminate funnel by turning
                    if (Perp2D(left, vr) < 0)
                    {
                        var polyLocalToWorld = query.PolygonLocalToWorldMatrix(polygons[apexIndex]);
                        var termPos = polyLocalToWorld.MultiplyPoint(apex + left);

                        RetracePortals(query, apexIndex, leftIndex, termPos, polygons, ref wayPoints);
                        if (vertexSides.IsCreated)
                            vertexSides.Add(-1);
                        
                        apex = polyWorldToLocal.MultiplyPoint(termPos);
                        left.Set(0, 0, 0);
                        right.Set(0, 0, 0);
                        i = apexIndex = leftIndex;
                        continue;
                    }
                    if (Perp2D(right, vl) > 0)
                    {
                        var polyLocalToWorld = query.PolygonLocalToWorldMatrix(polygons[apexIndex]);
                        var termPos = polyLocalToWorld.MultiplyPoint(apex + right);

                        RetracePortals(query, apexIndex, rightIndex, termPos, polygons, ref wayPoints);
                        if (vertexSides.IsCreated)
                            vertexSides.Add(1);

                        //Debug.Log("RIGHT");
                        
                        apex = polyWorldToLocal.MultiplyPoint(termPos);
                        left.Set(0, 0, 0);
                        right.Set(0, 0, 0);
                        i = apexIndex = rightIndex;

                        continue;
                    }

                    // Narrow funnel
                    if (Perp2D(left, vl) >= 0)
                    {
                        left = vl;
                        leftIndex = i;
                    }
                    if (Perp2D(right, vr) <= 0)
                    {
                        right = vr;
                        rightIndex = i;
                    }
                }
            }

            int pathIndex = wayPoints.Length - 1;
            if (wayPoints.Length > 0 && wayPoints[pathIndex].location.position == endPos)
                wayPoints.RemoveAt(pathIndex);

            RetracePortals(query, apexIndex, pathSize - 1, endPos, polygons, ref wayPoints);
            if (vertexSides.IsCreated)
                vertexSides.Add(0);

            // Fix flag for final path point

            pathIndex = wayPoints.Length - 1;
            var wayPoint = wayPoints[pathIndex];
            wayPoint.flag |= NavMeshWayPointFlag.End;
            wayPoints[pathIndex] = wayPoint;
            
            return PathQueryStatus.Success;
        }
    }
}