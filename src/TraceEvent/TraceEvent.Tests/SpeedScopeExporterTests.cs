﻿using Microsoft.Diagnostics.Tracing.Stacks;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace TraceEventTests
{
    public class SpeedScopeExporterTests
    {
        [Fact]
        public void GetSortedSamplesReturnsSamplesSortedByRelativeTime()
        {
            var sourceSamples = new[] {
                new FakeStackSourceSample(0.3),
                new FakeStackSourceSample(0.1),
                new FakeStackSourceSample(0.2)
            };

            var stackSource = new StackSourceStub(sourceSamples);

            var result = SpeedScopeExporter.GetSortedSamples(stackSource);

            Assert.Equal(0.1, result[0].RelativeTime);
            Assert.Equal(0.2, result[1].RelativeTime);
            Assert.Equal(0.3, result[2].RelativeTime);
        }

        [Fact]
        public void WalkTheStackAndExpandSamplesProducesFullInformation()
        {
            // Main() calls A() calls B()
            const double relativeTime = 0.1;
            var main = new FakeStackSourceSample(
                relativeTime: relativeTime,
                name: "Main",
                frameIndex: (StackSourceFrameIndex)5, // 5 is first non-taken enum value
                stackIndex: (StackSourceCallStackIndex)1, // 1 is first non-taken enum value
                callerIndex: StackSourceCallStackIndex.Invalid);
            var a = new FakeStackSourceSample(
                relativeTime: relativeTime,
                name: "A",
                frameIndex: (StackSourceFrameIndex)6,
                stackIndex: (StackSourceCallStackIndex)2,
                callerIndex: main.StackIndex);
            var b = new FakeStackSourceSample(
                relativeTime: relativeTime,
                name: "B",
                frameIndex: (StackSourceFrameIndex)7,
                stackIndex: (StackSourceCallStackIndex)3,
                callerIndex: a.StackIndex);

            var allSamples = new[] { main, a, b };
            var leafs = new[] { new SpeedScopeExporter.Sample(b.StackIndex, b.RelativeTime, b.Metric, -1) };
            var stackSource = new StackSourceStub(allSamples);
            
            SpeedScopeExporter.WalkTheStackAndExpandSamples(stackSource, leafs, out var frameNameToId, out var frameIdToSamples);

            Assert.Equal(0, frameNameToId[main.Name]);
            Assert.Equal(1, frameNameToId[a.Name]);
            Assert.Equal(2, frameNameToId[b.Name]);

            Assert.All(frameIdToSamples.Select(pair => pair.Value), samples => Assert.Equal(relativeTime, samples.Single().RelativeTime));
            Assert.Equal(0, frameIdToSamples[0].Single().Depth);
            Assert.Equal(1, frameIdToSamples[1].Single().Depth);
            Assert.Equal(2, frameIdToSamples[2].Single().Depth);
        }

        [Theory]
        [InlineData(StackSourceFrameIndex.Broken)]
        [InlineData(StackSourceFrameIndex.Invalid)]
        public void WalkTheStackAndExpandSamplesHandlesBrokenStacks(StackSourceFrameIndex kind)
        {
            // Main() calls WRONG
            const double relativeTime = 0.1;
            var main = new FakeStackSourceSample(
                relativeTime: relativeTime,
                name: "Main",
                frameIndex: (StackSourceFrameIndex)5, // 5 is first non-taken enum value
                stackIndex: (StackSourceCallStackIndex)1, // 1 is first non-taken enum value
                callerIndex: StackSourceCallStackIndex.Invalid);
            var wrong = new FakeStackSourceSample(
                relativeTime: relativeTime,
                name: "WRONG",
                frameIndex: kind,
                stackIndex: (StackSourceCallStackIndex)2,
                callerIndex: main.StackIndex);

            var allSamples = new[] { main, wrong };
            var leafs = new[] { new SpeedScopeExporter.Sample(wrong.StackIndex, wrong.RelativeTime, wrong.Metric, -1) };
            var stackSource = new StackSourceStub(allSamples);

            SpeedScopeExporter.WalkTheStackAndExpandSamples(stackSource, leafs, out var frameNameToId, out var frameIdToSamples);

            Assert.Equal(0, frameNameToId[main.Name]);
            Assert.False(frameNameToId.ContainsKey(wrong.Name));

            var theOnlySample = frameIdToSamples.Single().Value.Single();
            Assert.Equal(relativeTime, theOnlySample.RelativeTime);
            Assert.Equal(0, theOnlySample.Depth);
        }

        [Fact]
        public void GetAggregatedSortedProfileEventsAggregates_ContinuousSamplesToOneEvent()
        {
            const double metric = 0.1;

            var samples = new[]
            {
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 0.1),
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 0.2),
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 0.3),
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 0.4),
            };

            var input = new Dictionary<int, List<SpeedScopeExporter.Sample>>() { { 0, samples.ToList() } };

            var aggregatedEvents = SpeedScopeExporter.GetAggregatedSortedProfileEvents(input);

            // we should have an Open at 0.1 (first on the list) and Close at 0.4 (last on the list)
            Assert.Equal(2, aggregatedEvents.Count);

            Assert.Equal(0.1, aggregatedEvents[0].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Open, aggregatedEvents[0].Type);
            Assert.Equal(0.4, aggregatedEvents[1].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Close, aggregatedEvents[1].Type);
        }

        [Fact]
        public void GetAggregatedSortedProfileEventsAggregates_ContinuousSamplesWithPausesToMultipleEvents()
        {
            const double metric = 0.1;

            var samples = new[]
            {
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 0.1),
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 0.2),

                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 0.7),

                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 1.1),
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 1.2),
                new SpeedScopeExporter.Sample((StackSourceCallStackIndex)1, metric: metric, depth: 0, relativeTime: 1.3),
            };

            var input = new Dictionary<int, List<SpeedScopeExporter.Sample>>() { { 0, samples.ToList() } };

            var aggregatedEvents = SpeedScopeExporter.GetAggregatedSortedProfileEvents(input);

            // we should have <0.1, 0.2> and <0.7, 0.75> (the tool would ignore <0.7, 0.7>) and <1.1, 1.3>
            Assert.Equal(6, aggregatedEvents.Count);

            Assert.Equal(0.1, aggregatedEvents[0].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Open, aggregatedEvents[0].Type);
            Assert.Equal(0.2, aggregatedEvents[1].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Close, aggregatedEvents[1].Type);

            Assert.Equal(0.7, aggregatedEvents[2].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Open, aggregatedEvents[2].Type);
            Assert.Equal(0.7 + metric / 2, aggregatedEvents[3].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Close, aggregatedEvents[3].Type);

            Assert.Equal(1.1, aggregatedEvents[4].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Open, aggregatedEvents[4].Type);
            Assert.Equal(1.3, aggregatedEvents[5].RelativeTime);
            Assert.Equal(SpeedScopeExporter.ProfileEventType.Close, aggregatedEvents[5].Type);
        }

        [Fact]
        public void CompareProfileEventsAllowsForSortingInTheOrderExpectedByTheSpeedScope()
        {
            var profileEvents = new List<SpeedScopeExporter.ProfileEvent>()
            {
                new SpeedScopeExporter.ProfileEvent(SpeedScopeExporter.ProfileEventType.Open, frameId: 0, depth: 0, relativeTime: 0.1),
                new SpeedScopeExporter.ProfileEvent(SpeedScopeExporter.ProfileEventType.Open, frameId: 1, depth: 1, relativeTime: 0.1),
                new SpeedScopeExporter.ProfileEvent(SpeedScopeExporter.ProfileEventType.Close, frameId: 1, depth: 1, relativeTime: 0.3),
                new SpeedScopeExporter.ProfileEvent(SpeedScopeExporter.ProfileEventType.Close, frameId: 0, depth: 0, relativeTime: 0.3),
            };

            profileEvents.Reverse(); // reverse to make sure that it does sort the elements in right way

            profileEvents.Sort(SpeedScopeExporter.CompareProfileEvents);

            Assert.Equal(SpeedScopeExporter.ProfileEventType.Open, profileEvents[0].Type);
            Assert.Equal(0.1, profileEvents[0].RelativeTime);
            Assert.Equal(0, profileEvents[0].Depth);

            Assert.Equal(SpeedScopeExporter.ProfileEventType.Open, profileEvents[1].Type);
            Assert.Equal(0.1, profileEvents[1].RelativeTime);
            Assert.Equal(1, profileEvents[1].Depth);

            Assert.Equal(SpeedScopeExporter.ProfileEventType.Close, profileEvents[2].Type);
            Assert.Equal(0.3, profileEvents[2].RelativeTime);
            Assert.Equal(1, profileEvents[2].Depth);

            Assert.Equal(SpeedScopeExporter.ProfileEventType.Close, profileEvents[3].Type);
            Assert.Equal(0.3, profileEvents[3].RelativeTime);
            Assert.Equal(0, profileEvents[3].Depth);
        }

        #region private
        internal class FakeStackSourceSample
        {
            public FakeStackSourceSample(double relativeTime) => RelativeTime = relativeTime;

            public FakeStackSourceSample(double relativeTime, string name, StackSourceFrameIndex frameIndex,
                StackSourceCallStackIndex stackIndex, StackSourceCallStackIndex callerIndex)
            {
                RelativeTime = relativeTime;
                Name = name;
                FrameIndex = frameIndex;
                StackIndex = stackIndex;
                CallerIndex = callerIndex;
            }

            #region private
            public double RelativeTime { get; }
            public float Metric { get; }
            public string Name { get; }
            public StackSourceFrameIndex FrameIndex { get; }
            public StackSourceCallStackIndex StackIndex { get; }
            public StackSourceCallStackIndex CallerIndex { get; }
            #endregion private
        }

        internal class StackSourceStub : StackSource
        {
            public StackSourceStub(IReadOnlyList<FakeStackSourceSample> fakeStackSourceSamples) => this.samples = fakeStackSourceSamples;

            public override int CallStackIndexLimit => samples.Count;

            public override int CallFrameIndexLimit => samples.Count;

            public override void ForEach(Action<StackSourceSample> callback)
            {
                foreach (var stackSourceSample in samples)
                {
                    callback(new StackSourceSample(this)
                    {
                        TimeRelativeMSec = stackSourceSample.RelativeTime,
                        Metric = stackSourceSample.Metric,
                        StackIndex = stackSourceSample.StackIndex
                    });
                }
            }

            public override StackSourceCallStackIndex GetCallerIndex(StackSourceCallStackIndex callStackIndex)
                => samples.First(sample => sample.StackIndex == callStackIndex).CallerIndex;

            public override StackSourceFrameIndex GetFrameIndex(StackSourceCallStackIndex callStackIndex)
                => samples.First(sample => sample.StackIndex == callStackIndex).FrameIndex;

            public override string GetFrameName(StackSourceFrameIndex frameIndex, bool verboseName)
                => samples.First(sample => sample.FrameIndex == frameIndex).Name;

            #region private
            private readonly IReadOnlyList<FakeStackSourceSample> samples;
            #endregion private
        }
        #endregion private
    }
}
