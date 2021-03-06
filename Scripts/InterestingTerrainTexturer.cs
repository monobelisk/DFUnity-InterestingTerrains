using UnityEngine;
using System.Collections;
using DaggerfallWorkshop;
using Unity.Jobs;
using System;
using DaggerfallConnect.Arena2;
using Unity.Collections;
using DaggerfallWorkshop.Game.Utility.ModSupport;

namespace Monobelisk
{
    public class InterestingTerrainTexturer : ITerrainTexturing
    {
        protected static readonly int tileDataDim = MapsFile.WorldMapTileDim + 1;
        protected static readonly int assignTilesDim = MapsFile.WorldMapTileDim;
        protected byte[] lookupTable;

        public InterestingTerrainTexturer()
        {
            CreateLookupTable();
        }

        /// <summary>
        /// Works exactly like the default TerrainTexturer, except that it skips the GenerateTileDataJob
        /// and uses the tileData generated during terrain sampling instead.
        /// Use the mod messaging system to obtain tileData for a map pixel in a custom TerrainTexturer.
        /// </summary>
        /// <param name="terrainSampler"></param>
        /// <param name="mapData"></param>
        /// <param name="dependencies"></param>
        /// <param name="march"></param>
        /// <returns></returns>
        public virtual JobHandle ScheduleAssignTilesJob(ITerrainSampler terrainSampler, ref MapPixelData mapData, JobHandle dependencies, bool march = true)
        {
            // Load tile data generated by the Terrain Sampler
            var tData = InterestingTerrains.tileDataCache.Get(mapData.mapPixelX, mapData.mapPixelY);

            /// To access the tileData in a separate mod, you can use this code instead:
            //byte[] tData = null;
            //ModManager.Instance.SendModMessage("Interesting Terrains", "getTileData", new int[] { mapData.mapPixelX, mapData.mapPixelY }, (string message, object data) =>
            //{
            //    if (message == "error")
            //    {
            //        Debug.LogError(data as string);
            //    }
            //    else
            //    {
            //        tData = data as byte[];
            //    }
            //});

            NativeArray<byte> tileData = new NativeArray<byte>(tData, Allocator.TempJob);

            // Assign tile data to terrain
            NativeArray<byte> lookupData = new NativeArray<byte>(lookupTable, Allocator.TempJob);
            AssignTilesJob assignTilesJob = new AssignTilesJob
            {
                lookupTable = lookupData,
                tileData = tileData,
                tilemapData = mapData.tilemapData,
                tdDim = tileDataDim,
                tDim = assignTilesDim,
                march = march,
                locationRect = mapData.locationRect,
            };
            JobHandle assignTilesHandle = assignTilesJob.Schedule(assignTilesDim * assignTilesDim, 64, dependencies);

            // Add both working native arrays to disposal list.
            mapData.nativeArrayList.Add(tileData);
            mapData.nativeArrayList.Add(lookupData);

            return assignTilesHandle;
        }

        #region Marching Squares - WIP

        // Very basic marching squares for water > dirt > grass > stone transitions.
        // Cannot handle water > grass or water > stone, etc.
        // Will improve this at later date to use a wider range of transitions.
        protected struct AssignTilesJob : IJobParallelFor
        {
            [ReadOnly]
            public NativeArray<byte> tileData;
            [ReadOnly]
            public NativeArray<byte> lookupTable;

            public NativeArray<byte> tilemapData;

            public int tdDim;
            public int tDim;
            public bool march;
            public Rect locationRect;

            public void Execute(int index)
            {
                int x = JobA.Row(index, tDim);
                int y = JobA.Col(index, tDim);

                // Do nothing if in location rect as texture already set, to 0xFF if zero
                if (tilemapData[index] != 0)
                    return;

                // Get sample points
                int tdIdx = JobA.Idx(x, y, tdDim);
                int b0 = tileData[tdIdx];               // tileData[x, y]
                int b1 = tileData[tdIdx + 1];           // tileData[x + 1, y]
                int b2 = tileData[tdIdx + tdDim];       // tileData[x, y + 1]
                int b3 = tileData[tdIdx + tdDim + 1];   // tileData[x + 1, y + 1]

                int shape = (b0 & 1) | (b1 & 1) << 1 | (b2 & 1) << 2 | (b3 & 1) << 3;
                int ring = (b0 + b1 + b2 + b3) >> 2;
                int tileID = shape | ring << 4;

                if (tileID > lookupTable.Length - 1)
                    return;

                tilemapData[index] = lookupTable[tileID];
            }
        }

        // Creates lookup table
        void CreateLookupTable()
        {
            lookupTable = new byte[64];
            AddLookupRange(0, 1, 5, 48, false, 0);
            AddLookupRange(2, 1, 10, 51, true, 16);
            AddLookupRange(2, 3, 15, 53, false, 32);
            AddLookupRange(3, 3, 15, 53, true, 48);
        }

        // Adds range of 16 values to lookup table
        void AddLookupRange(int baseStart, int baseEnd, int shapeStart, int saddleIndex, bool reverse, int offset)
        {
            if (reverse)
            {
                // high > low
                lookupTable[offset] = MakeLookup(baseStart, false, false);
                lookupTable[offset + 1] = MakeLookup(shapeStart + 2, true, true);
                lookupTable[offset + 2] = MakeLookup(shapeStart + 2, false, false);
                lookupTable[offset + 3] = MakeLookup(shapeStart + 1, true, true);
                lookupTable[offset + 4] = MakeLookup(shapeStart + 2, false, true);
                lookupTable[offset + 5] = MakeLookup(shapeStart + 1, false, true);
                lookupTable[offset + 6] = MakeLookup(saddleIndex, true, false); //d
                lookupTable[offset + 7] = MakeLookup(shapeStart, true, true);
                lookupTable[offset + 8] = MakeLookup(shapeStart + 2, true, false);
                lookupTable[offset + 9] = MakeLookup(saddleIndex, false, false); //d
                lookupTable[offset + 10] = MakeLookup(shapeStart + 1, false, false);
                lookupTable[offset + 11] = MakeLookup(shapeStart, false, false);
                lookupTable[offset + 12] = MakeLookup(shapeStart + 1, true, false);
                lookupTable[offset + 13] = MakeLookup(shapeStart, false, true);
                lookupTable[offset + 14] = MakeLookup(shapeStart, true, false);
                lookupTable[offset + 15] = MakeLookup(baseEnd, false, false);
            }
            else
            {
                // low > high
                lookupTable[offset] = MakeLookup(baseStart, false, false);
                lookupTable[offset + 1] = MakeLookup(shapeStart, true, false);
                lookupTable[offset + 2] = MakeLookup(shapeStart, false, true);
                lookupTable[offset + 3] = MakeLookup(shapeStart + 1, true, false);
                lookupTable[offset + 4] = MakeLookup(shapeStart, false, false);
                lookupTable[offset + 5] = MakeLookup(shapeStart + 1, false, false);
                lookupTable[offset + 6] = MakeLookup(saddleIndex, false, false); //d
                lookupTable[offset + 7] = MakeLookup(shapeStart + 2, true, false);
                lookupTable[offset + 8] = MakeLookup(shapeStart, true, true);
                lookupTable[offset + 9] = MakeLookup(saddleIndex, true, false); //d
                lookupTable[offset + 10] = MakeLookup(shapeStart + 1, false, true);
                lookupTable[offset + 11] = MakeLookup(shapeStart + 2, false, true);
                lookupTable[offset + 12] = MakeLookup(shapeStart + 1, true, true);
                lookupTable[offset + 13] = MakeLookup(shapeStart + 2, false, false);
                lookupTable[offset + 14] = MakeLookup(shapeStart + 2, true, true);
                lookupTable[offset + 15] = MakeLookup(baseEnd, false, false);
            }
        }

        // Encodes a byte with Daggerfall tile lookup
        byte MakeLookup(int index, bool rotate, bool flip)
        {
            if (index > 55)
                throw new IndexOutOfRangeException("Index out of range. Valid range 0-55");
            if (rotate) index += 64;
            if (flip) index += 128;

            return (byte)index;
        }

        #endregion
    }
}
