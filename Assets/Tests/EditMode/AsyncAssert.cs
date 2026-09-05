using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Sim.Tests.EditMode
{
    /// <summary>
    /// The Unity-bundled NUnit build this project's Test Runner actually uses
    /// (com.unity.ext.nunit@1.0.6, the net35 "unity-custom" assembly) ships with no async
    /// assertion methods at all — Assert.ThrowsAsync/DoesNotThrowAsync/CatchAsync are all
    /// absent (confirmed absent from the assembly itself, not just undocumented). Every test
    /// file that needs the ThrowsAsync&lt;TException&gt; shape used one anyway, which only
    /// surfaced as a compile error the first time this project was actually opened in Unity.
    ///
    /// This is the one small shared helper reproducing just that shape, so five call sites don't
    /// each duplicate the same try/catch. DoesNotThrowAsync needed no replacement of its own —
    /// every site using it was already inside an `async Task` test method, so awaiting the call
    /// directly (letting a real exception fail the test with its own message) is strictly better
    /// than re-wrapping it.
    /// </summary>
    internal static class AsyncAssert
    {
        public static async Task<TException> ThrowsAsync<TException>(Func<Task> action) where TException : Exception
        {
            try
            {
                await action();
            }
            catch (TException expected)
            {
                return expected;
            }
            catch (Exception unexpected)
            {
                Assert.Fail($"Expected {typeof(TException).Name} but caught {unexpected.GetType().Name}: {unexpected.Message}");
                return null;
            }

            Assert.Fail($"Expected {typeof(TException).Name} but no exception was thrown.");
            return null;
        }
    }
}
