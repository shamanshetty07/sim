using NUnit.Framework;
using Sim.Gameplay;

namespace Sim.Tests.EditMode
{
    /// <summary>CourseResult is a plain immutable data holder — these tests just confirm every constructor argument lands on the matching property, unchanged, with no derived/recalculated value.</summary>
    public class CourseResultTests
    {
        [Test]
        public void Constructor_CapturesEveryFieldExactly()
        {
            var result = new CourseResult(
                elapsedSeconds: 102.372f,
                completedCheckpoints: 15,
                totalCheckpoints: 15,
                recoveryCount: 2,
                isCompleted: true,
                worldSeed: 123456789);

            Assert.AreEqual(102.372f, result.ElapsedSeconds, 0.0001f);
            Assert.AreEqual(15, result.CompletedCheckpoints);
            Assert.AreEqual(15, result.TotalCheckpoints);
            Assert.AreEqual(2, result.RecoveryCount);
            Assert.IsTrue(result.IsCompleted);
            Assert.AreEqual(123456789, result.WorldSeed);
        }

        [Test]
        public void Fields_HaveNoPublicSetters()
        {
            // Compile-time immutability check as much as a runtime one: CourseResult exposes
            // get-only properties, so there is no API surface for later gameplay code to mutate
            // a result after construction. Reflection here only asserts what the compiler already
            // enforces, as a regression guard against a future edit accidentally adding a setter.
            System.Reflection.PropertyInfo[] properties = typeof(CourseResult).GetProperties();
            foreach (System.Reflection.PropertyInfo property in properties)
                Assert.IsFalse(property.CanWrite, $"{property.Name} must not have a public setter — CourseResult is a one-time snapshot.");
        }
    }
}
