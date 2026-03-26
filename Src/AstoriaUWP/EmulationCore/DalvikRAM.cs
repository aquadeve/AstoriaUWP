using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DalvikUWPCSharp.Classes
{
    /// <summary>
    /// Manages heap memory for the Dalvik VM. Provides object allocation,
    /// reference tracking, and basic garbage collection for Android objects
    /// running in the emulator.
    /// </summary>
    public class DalvikRAM
    {
        private Dictionary<int, object> heap = new Dictionary<int, object>();
        private Dictionary<int, string> typeMap = new Dictionary<int, string>();
        private int nextHandle = 1;
        private long totalAllocated;
        private long maxHeapSize;

        public DalvikRAM() : this(256 * 1024 * 1024) { } // 256 MB default

        public DalvikRAM(long maxHeap)
        {
            maxHeapSize = maxHeap;
        }

        /// <summary>
        /// Allocates an object on the Dalvik heap and returns its handle.
        /// </summary>
        public int Allocate(object obj, string typeName)
        {
            int handle = nextHandle++;
            heap[handle] = obj;
            typeMap[handle] = typeName;
            totalAllocated += EstimateSize(obj);
            return handle;
        }

        /// <summary>
        /// Retrieves an object from the heap by its handle.
        /// </summary>
        public object Get(int handle)
        {
            if (heap.TryGetValue(handle, out object obj))
                return obj;
            return null;
        }

        /// <summary>
        /// Frees an object from the heap.
        /// </summary>
        public void Free(int handle)
        {
            if (heap.TryGetValue(handle, out object obj))
            {
                totalAllocated -= EstimateSize(obj);
                heap.Remove(handle);
                typeMap.Remove(handle);
            }
        }

        /// <summary>
        /// Gets the type name of an object on the heap.
        /// </summary>
        public string GetTypeName(int handle)
        {
            if (typeMap.TryGetValue(handle, out string name))
                return name;
            return null;
        }

        /// <summary>
        /// Returns the number of live objects on the heap.
        /// </summary>
        public int ObjectCount => heap.Count;

        /// <summary>
        /// Returns approximate total allocated memory in bytes.
        /// </summary>
        public long TotalAllocated => totalAllocated;

        /// <summary>
        /// Performs a simple garbage collection pass - removes null references.
        /// </summary>
        public int CollectGarbage()
        {
            var toRemove = new List<int>();
            foreach (var kvp in heap)
            {
                if (kvp.Value == null)
                    toRemove.Add(kvp.Key);
            }

            foreach (int handle in toRemove)
                Free(handle);

            Debug.WriteLine("[DalvikRAM] GC collected " + toRemove.Count + " objects, " +
                heap.Count + " remaining");
            return toRemove.Count;
        }

        private static long EstimateSize(object obj)
        {
            if (obj == null) return 0;
            if (obj is string s) return s.Length * 2 + 16;
            if (obj is byte[] ba) return ba.Length + 16;
            if (obj is Array arr) return arr.Length * 8 + 16;
            return 64; // Default estimate for generic objects
        }
    }
}
