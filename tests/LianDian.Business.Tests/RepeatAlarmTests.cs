using System;
using LianDian.Core.Logging;
using Xunit;

namespace LianDian.Business.Tests
{
    public class RepeatAlarmTests
    {
        [Fact]
        public void LimitsDuplicatesButReportsNewErrorsAndRecovery()
        {
            DateTime now = new DateTime(2026, 9, 10);
            var alarm = new RepeatAlarm(() => now);
            Assert.True(alarm.ShouldReport("A"));
            Assert.False(alarm.ShouldReport("A"));
            Assert.True(alarm.ShouldReport("B"));
            now = now.AddSeconds(30);
            Assert.True(alarm.ShouldReport("A"));
            Assert.True(alarm.Reset());
            Assert.False(alarm.Reset());
            Assert.True(alarm.ShouldReport("A"));
        }
    }
}
