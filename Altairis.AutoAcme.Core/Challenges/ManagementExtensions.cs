using System;
using System.Collections.Generic;
using System.Management;
using System.Threading.Tasks;

namespace Altairis.AutoAcme.Core.Challenges {
    public static class ManagementExtensions {
        private static CompletedEventHandler CreateCompletedHandler<T>(this TaskCompletionSource<T> that, T result = default(T)) {
            return (sender, eventArgs) => {
                switch (eventArgs.Status) {
                case ManagementStatus.NoError:
                case ManagementStatus.False:
                    that.SetResult(result);
                    break;
                case ManagementStatus.OperationCanceled:
                    that.SetCanceled();
                    break;
                default:
                    that.SetException(new ManagementException(eventArgs.Status.ToString()));
                    break;
                }
            };
        }

        public static Task<IReadOnlyList<ManagementBaseObject>> InvokeMethodAsync(this ManagementClass that, string name, params object[] args) {
            var objects = new List<ManagementBaseObject>();
            var taskSource = new TaskCompletionSource<IReadOnlyList<ManagementBaseObject>>();
            var watcher = new ManagementOperationObserver();
            watcher.ObjectReady += (sender, eventArgs) => objects.Add(eventArgs.NewObject);
            watcher.Completed += taskSource.CreateCompletedHandler(objects);
            that.InvokeMethod(watcher, name, args);
            return taskSource.Task;
        }

        public static Task DeleteAsync(this ManagementObject that) {
            var taskSource = new TaskCompletionSource<object>();
            var watcher = new ManagementOperationObserver();
            watcher.Completed += taskSource.CreateCompletedHandler();
            that.Delete(watcher);
            return taskSource.Task;
        }
    }
}
