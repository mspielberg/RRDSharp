using System;
using System.Collections.Generic;
using System.Linq;

namespace RRD
{
    public class CircBuffer<T>
    {
        protected readonly T[] data;
        protected int tail;

        public CircBuffer(int size)
        {
            data = new T[size];
            tail = 0;
        }

        public virtual int Length => data.Length;

        public virtual IEnumerable<T> Values => Slice(tail, Length);

        public virtual void Push(T value)
        {
            data[tail] = value;
            tail = (tail + 1) % data.Length;
        }

        public T this[int ago]
        {
            get
            {
                if (ago >= data.Length)
                    throw new ArgumentOutOfRangeException(nameof(ago));
                return data[(data.Length + (tail - ago - 1)) % data.Length];
            }
        }

        public override string ToString()
        {
            var firstPart = new ArraySegment<T>(data, tail, data.Length - tail);
            var secondPart = new ArraySegment<T>(data, 0, tail);
            return $"[{string.Join(",", Enumerable.Concat(firstPart, secondPart))}]";
        }

        protected virtual IEnumerable<T> Slice(int start, int count)
        {
            if (start < 0 || start >= data.Length)
                throw new ArgumentOutOfRangeException(nameof(start));
            if (count > data.Length)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (start + count <= data.Length)
                return new ArraySegment<T>(data, start, count);
            return Enumerable.Concat(
                new ArraySegment<T>(data, start, data.Length - start),
                new ArraySegment<T>(data, 0, count - data.Length + start));
        }
    }

    public delegate T Reducer<T>(IEnumerable<T> values);

    public class ReducingBuffer<T> : CircBuffer<T>
    {
        private readonly int reducingFactor;
        private readonly Reducer<T> reducer;
        private readonly CircBuffer<T> child;

        public ReducingBuffer(int size, int reducingFactor, Reducer<T> reducer, CircBuffer<T> child)
            : base(size + reducingFactor)
        {
            if (size <= 0)
                throw new ArgumentOutOfRangeException(nameof(size));
            if (child != null && reducingFactor <= 0)
                throw new ArgumentOutOfRangeException(nameof(reducingFactor));
            if (size % reducingFactor != 0)
                throw new ArgumentException($"{nameof(size)} must be a multiple of {nameof(reducingFactor)}", nameof(size));

            this.reducingFactor = reducingFactor;
            this.reducer = reducer;
            this.child = child;
        }
        
        public override int Length => data.Length - reducingFactor;

        public override void Push(T value)
        {
            base.Push(value);
            if (child != null && tail % reducingFactor == 0)
            {
                // accumulated enough data for a reduced data point
                child.Push(reducer(ValuesToReduce));
            }
        }

        public override string ToString() => $"[{string.Join(",", Values)}]";

        private int OldestValueIndex => (tail + reducingFactor) % data.Length;

        public override IEnumerable<T> Values => Slice(OldestValueIndex, Length);

        public IEnumerable<T> ValuesToReduce => Slice(tail, reducingFactor);
    }

    public class RRD<T>
    {
        private readonly int samplesPerLevel;
        private readonly int reducingFactor;
        private readonly ReducingBuffer<T>[] buffers;

        private readonly int numDataPoints;
        private readonly int numSamples;

        public RRD(int numLevels, int dataPointsInLastLevel, int reducingFactor, Reducer<T> reducer)
        {
            this.reducingFactor = reducingFactor;
            buffers = CreateBuffers(numLevels, dataPointsInLastLevel, reducingFactor, reducer).ToArray();
            samplesPerLevel = buffers[numLevels - 1].Length;

            numDataPoints = buffers.Select(b => b.Length).Sum();
            numSamples = samplesPerLevel * numLevels;
        }

        ReducingBuffer<T> NewestBuffer => buffers[buffers.Length - 1];

        public void Push(T value)
        {
            NewestBuffer.Push(value);
        }

        public int CapacityDataPoints => numDataPoints;

        public int CapacitySamples => numSamples;

        public T GetDataPoint(int index)
        {
            int level = buffers.Length - 1;
            while (index >= buffers[level].Length)
            {
                index -= buffers[level].Length;
                level--;
            }
            return buffers[level][index];
        }

        public T GetSample(int ago)
        {
            int level = ago / samplesPerLevel;
            if (level >= buffers.Length)
                throw new ArgumentOutOfRangeException(nameof(ago));
            int dataPointsPerSample = 1;
            for (int i = 0; i < level; i++)
                dataPointsPerSample *= reducingFactor;
            int sampleWithinLevel = ago % samplesPerLevel;
            var dataPointWithinLevel = sampleWithinLevel / dataPointsPerSample;
            return buffers[buffers.Length - level - 1][dataPointWithinLevel];
        }

        public override string ToString() =>
            string.Join(",", buffers.Select((b, i) => $"{i}: {b}"));

        private static IEnumerable<ReducingBuffer<T>> CreateBuffers(int numLevels, int dataPointsInLastLevel, int reducingFactor, Reducer<T> reducer)
        {
            ReducingBuffer<T> prevBuffer = null;
            foreach (int bufferSize in GetSizes(numLevels, dataPointsInLastLevel, reducingFactor))
            {
                var buffer = new ReducingBuffer<T>(bufferSize, reducingFactor, reducer, prevBuffer);
                yield return buffer;
                prevBuffer = buffer;
            }
        }

        /// <summary>
        /// Returns number of stored data points in each level, from smallest (for oldest data) to largest (for newest data).
        /// </summary>
        private static IEnumerable<int> GetSizes(int numLevels, int dataPointsInLastLevel, int reducingFactor)
        {
            var dataPoints = dataPointsInLastLevel;
            for (int i = 0; i < numLevels; i++)
            {
                yield return dataPoints;
                dataPoints *= reducingFactor;
            }
        }
    }
}
