using System;
using System.Collections.Generic;
using System.Threading;
using Windows.Foundation;

namespace WinCal
{
    // Minimal WinRT async helpers. The legacy csc cannot reference
    // System.Runtime.WindowsRuntime.dll (it requires the aggregated Windows.winmd
    // from the Windows SDK), so we wait on IAsyncOperation.Completed ourselves.
    internal static class Rt
    {
        public static T Await<T>(IAsyncOperation<T> op)
        {
            using (var evt = new ManualResetEvent(false))
            {
                op.Completed = delegate { evt.Set(); };
                evt.WaitOne();
                if (op.Status == AsyncStatus.Completed) return op.GetResults();
                if (op.Status == AsyncStatus.Error) throw op.ErrorCode;
                throw new OperationCanceledException();
            }
        }

        public static void Await(IAsyncAction action)
        {
            using (var evt = new ManualResetEvent(false))
            {
                action.Completed = delegate { evt.Set(); };
                evt.WaitOne();
                if (action.Status == AsyncStatus.Error) throw action.ErrorCode;
                if (action.Status == AsyncStatus.Canceled) throw new OperationCanceledException();
            }
        }
    }
}
