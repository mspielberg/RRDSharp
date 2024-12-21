using System.Collections.Generic;
using System.Linq;

namespace RRD.Test
{
    public class RRDTest
    {
        [Fact]
        public void CircBufferToString()
        {
            var uut = new CircBuffer<int>(4);
            for (int i = 0; i < 10; i++)
                uut.Push(i);
            Assert.Equal(9, uut[0]);
            Assert.Equal(8, uut[1]);
            Assert.Equal(7, uut[2]);
            Assert.Equal(6, uut[3]);
            Assert.Equal(new List<int>() { 6, 7, 8, 9 }, uut.Values);
        }

        [Fact]
        public void PushIntoReducingBuffer()
        {
            var uut = new ReducingBuffer<int>(4, 2, Enumerable.Min, null);
            for (int i = 0; i < 6; i++)
                uut.Push(i);
            Assert.Equal(new List<int>() { 2, 3, 4, 5 }, uut.Values.ToList());
            Assert.Equal(new List<int>() { 0, 1 }, uut.ValuesToReduce.ToList());
        }

        [Fact]
        public void PushIntoChild()
        {
            var child = new CircBuffer<float>(2);
            var uut = new ReducingBuffer<float>(4, 2, Enumerable.Average, child);
            for (int i = 0; i < 10; i++)
                uut.Push(i);
            Assert.Equal(Enumerable.Range(6, 4).Select(x => (float) x).ToList(), uut.Values.ToList());
            Assert.Equal(new List<float>() { 2.5f, 4.5f }, child.Values.ToList());
        }

        [Fact]
        public void RRDLengths()
        {
            var uut = new RRD<float>(3, 3, 3, Enumerable.Average);
            Assert.Equal(27 * 3, uut.CapacitySamples);
            Assert.Equal(27 + 9 + 3, uut.CapacityDataPoints);
        }

        [Fact]
        public void PushIntoRRD()
        {
            var uut = new RRD<float>(3, 3, 3, Enumerable.Average);
            for (int i = 0; i < 27 * 3; i++)
                uut.Push(i);

            var expectedDataPointsDescriptions = new (int repetitions, IEnumerable<float> values)[]
            {
                // level 0, (80, 79, 78, ... 55)
                (1, Enumerable.Range(0, 27).Select(x => 80f - x)),
                // level 1, each an average of 3 consecutive values, (52, 49, 45, ... 28)
                (3, Enumerable.Range(0, 9).Select(x => 28f + 3f * (8f - x))),
                // level 2, each an average of 9 consecutive values, (22, 13, 4)
                (9, Enumerable.Range(0, 3).Select(x => 4f + 9f * (2f - x))),
            };

            var expectedSamples =
                expectedDataPointsDescriptions.SelectMany(d =>
                    d.values.SelectMany(v =>
                        Enumerable.Repeat(v, d.repetitions)));

            var actualSamples = Enumerable.Range(0, uut.CapacitySamples).Select(uut.GetSample).ToList();

            Assert.Equal(expectedSamples.ToList(), actualSamples);

            var expectedDataPoints =
                expectedDataPointsDescriptions.SelectMany(d => d.values);

            var actualDataPoints = Enumerable.Range(0, uut.CapacityDataPoints).Select(uut.GetDataPoint).ToList();

            Assert.Equal(expectedDataPoints.ToList(), actualDataPoints);
        }
    }
}