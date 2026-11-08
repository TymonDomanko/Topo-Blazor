using Topo.Model.Common;

namespace TopoReportFunctionTest
{
    [TestClass]
    public class AutoRunTests
    {
        [TestMethod]
        public async Task OnlyTheLastOfSeveralQuickChangesReads()
        {
            using var auto = new AutoRun();
            var reads = new List<int>();

            var first = auto.RunAsync(_ => { reads.Add(1); return Task.CompletedTask; }, 100);
            var second = auto.RunAsync(_ => { reads.Add(2); return Task.CompletedTask; }, 100);
            var third = auto.RunAsync(_ => { reads.Add(3); return Task.CompletedTask; }, 100);
            await Task.WhenAll(first, second, third);

            CollectionAssert.AreEqual(new[] { 3 }, reads);
        }

        [TestMethod]
        public async Task ARunningReadIsToldToStopAndTheNextWaitsForIt()
        {
            using var auto = new AutoRun();
            var events = new List<string>();
            var started = new TaskCompletionSource();

            var first = auto.RunAsync(async token =>
            {
                events.Add("first started");
                started.SetResult();
                try
                {
                    await Task.Delay(Timeout.Infinite, token);
                }
                finally
                {
                    events.Add("first stopped");
                }
            });
            await started.Task;
            var second = auto.RunAsync(_ => { events.Add("second ran"); return Task.CompletedTask; });
            await Task.WhenAll(first, second);

            CollectionAssert.AreEqual(new[] { "first started", "first stopped", "second ran" }, events);
        }

        [TestMethod]
        public async Task AReadThatFailedDoesNotStopTheNextOne()
        {
            using var auto = new AutoRun();
            var ran = false;

            var failing = auto.RunAsync(_ => throw new InvalidOperationException("Terrain was unreachable"));
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => failing);
            await auto.RunAsync(_ => { ran = true; return Task.CompletedTask; });

            Assert.IsTrue(ran);
        }

        [TestMethod]
        public async Task StoppingLeavesNothingToRun()
        {
            using var auto = new AutoRun();
            var ran = false;

            var pending = auto.RunAsync(_ => { ran = true; return Task.CompletedTask; }, 100);
            auto.Stop();
            await pending;

            Assert.IsFalse(ran);
        }
    }
}
