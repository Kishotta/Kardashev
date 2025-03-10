using System;
using Unity.Collections;

namespace Kardashev
{
	public struct NativePriorityQueue<T>
		where T : unmanaged, IComparable<T>
	{
		private NativeList<T> heap;

		public NativePriorityQueue(int capacity, Allocator allocator)
		{
			heap = new NativeList<T>(capacity, allocator);
		}

		public void Enqueue(T item)
		{
			heap.Add(item);
			var ci = heap.Length - 1;
			while (ci > 0)
			{
				var pi = (ci - 1) / 2;
				if (heap[pi].CompareTo(heap[pi]) >= 0)
					break;
				(heap[ci], heap[pi]) = (heap[pi], heap[ci]);
				ci                   = pi;
			}
		}

		public T Dequeue()
		{
			var li = heap.Length - 1;
			var frontItem = heap[0];
			heap[0] = heap[li];
			heap.RemoveAt(li);
			li--;
			var pi = 0;
			while (true)
			{
				var ci = 2 * pi + 1;
				if (ci > li)
					break;
				var rc = ci + 1;
				if(rc <= li && heap[rc].CompareTo(heap[ci]) < 0)
					ci = rc;
				if (heap[pi].CompareTo(heap[ci]) <= 0)
					break;
				(heap[pi], heap[ci]) = (heap[ci], heap[pi]);
				pi                   = ci;
			}

			return frontItem;
		}

		public bool IsEmpty()
		{
			return heap.Length == 0;
		}

		public void Dispose()
		{
			if (heap.IsCreated) heap.Dispose();
		}
	}
}