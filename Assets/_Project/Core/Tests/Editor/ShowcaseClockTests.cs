using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PoOlympic.Tests
{
    /// <summary>The attract-mode idle clock (and the camera's shot dwell) skip long frames; each brain logs one
    /// "inference running" line; the benchmark names the hardware it measured.</summary>
    public class ShowcaseClockTests
    {
        [Test]
        public void IdleClock_CountsNormalFrames()
        {
            var c = new IdleClock();
            for (int i = 0; i < 60; i++) c.Advance(1f / 60f);
            Assert.AreEqual(1f, c.Seconds, 1e-4f);
            c.Advance(IdleClock.MaxFrameSeconds);
            Assert.AreEqual(1.25f, c.Seconds, 1e-4f, "a frame of exactly 0.25 s still counts");
        }

        [Test]
        public void IdleClock_SkipsFramesLongerThanAQuarterSecond()
        {
            var c = new IdleClock();
            c.Advance(0.1f);
            c.Advance(5f);                                   // a scene load / background tab
            Assert.AreEqual(0.1f, c.Seconds, 1e-5f, "a 5 s frame must not count as idle time");
            for (int i = 0; i < 100; i++) c.Advance(1f);     // 100 s of 1 s frames: never reaches the 30 s attract time
            Assert.AreEqual(0.1f, c.Seconds, 1e-5f);
            Assert.Less(c.Seconds, ShowcaseSettings.Current.idleDemoSeconds);
            c.Advance(0.26f);
            c.Advance(-1f);
            c.Advance(float.NaN);
            Assert.AreEqual(0.1f, c.Seconds, 1e-5f, "0.26 s, negative and NaN frames are skipped");
            c.Reset();
            Assert.AreEqual(0f, c.Seconds);
        }

        [Test]
        public void IdleClock_ReachesTheAttractTimeOnlyWithShortFrames()
        {
            var c = new IdleClock();
            float idle = ShowcaseSettings.Current.idleDemoSeconds;
            int frames = 0;
            while (c.Seconds <= idle && frames < 100000) { c.Advance(frames % 10 == 0 ? 2f : 0.02f); frames++; }
            // 9 of 10 frames count 0.02 s: 30 s of counted time needs ~1667 frames, the long ones add nothing
            Assert.AreEqual(idle, c.Seconds, 0.05f);
            Assert.Greater(frames, (int)(idle / 0.02f));
        }

        [Test]
        public void IdleClock_StepIsWhatADwellTimerAdds()
        {
            Assert.AreEqual(0.016f, IdleClock.Step(0.016f));
            Assert.AreEqual(0f, IdleClock.Step(0.3f));
            Assert.AreEqual(0f, IdleClock.Step(0f));
        }

        [Test]
        public void InferenceLog_LogsEachBrainOnce()
        {
            InferenceLog.Clear();
            LogAssert.Expect(LogType.Log, new Regex(@"^\[PoOlympic\] rung2\.onnx inference running \(MATT"));
            Assert.IsTrue(InferenceLog.Note("rung2", "MATT, CPU, 84 obs -> 23 actions"));
            Assert.IsFalse(InferenceLog.Note("rung2", "MATT, CPU, 84 obs -> 23 actions"), "a brain logs its line once");
            LogAssert.Expect(LogType.Log, new Regex(@"^\[PoOlympic\] slope_kim\.onnx inference running"));
            Assert.IsTrue(InferenceLog.Note("slope_kim", "KIM"));
            Assert.IsFalse(InferenceLog.Note("", "no brain"));
            LogAssert.NoUnexpectedReceived();
            InferenceLog.Clear();
        }

        [Test]
        public void Benchmark_HardwareLabelNamesCpuAndGpu()
        {
            string hw = SimBenchmark.Hardware();
            StringAssert.Contains(SystemInfo.processorCount.ToString(), hw);
            StringAssert.Contains("CPU", hw);
            StringAssert.Contains("GPU", hw);
            if (!string.IsNullOrEmpty(SystemInfo.graphicsDeviceName)) StringAssert.Contains(SystemInfo.graphicsDeviceName, hw);
        }
    }
}
