// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using Azure.Functions.Cli.Interfaces;

namespace Azure.Functions.Cli.Common
{
    internal class ProcessManager : IProcessManager
    {
        private IList<Process> _childProcesses;
        private IList<string> _dockerContainers;

        public IProcessInfo GetCurrentProcess()
        {
            return new ProcessInfo(Process.GetCurrentProcess());
        }

        public IProcessInfo GetProcessById(int processId)
        {
            return new ProcessInfo(Process.GetProcessById(processId));
        }

        public IEnumerable<IProcessInfo> GetProcessesByName(string processName)
        {
            return Process.GetProcessesByName(processName)
                .Select(p => new ProcessInfo(p));
        }

        public void KillChildProcesses()
        {
            if (_childProcesses == null)
            {
                return;
            }

            foreach (var childProcess in _childProcesses)
            {
                if (!childProcess.HasExited)
                {
                    childProcess.Kill();
                }
            }
        }

        public bool RegisterChildProcess(Process childProcess)
        {
            _childProcesses ??= new List<Process>();

            // be graceful if someone calls this method with the same process multiple times.
            if (_childProcesses.Any(p => p.Id == childProcess.Id))
            {
                return false;
            }

            _childProcesses.Add(childProcess);
            return true;
        }

        public void RegisterDockerContainer(string containerId)
        {
            if (string.IsNullOrEmpty(containerId))
            {
                return;
            }

            _dockerContainers ??= new List<string>();

            if (!_dockerContainers.Contains(containerId))
            {
                _dockerContainers.Add(containerId);
            }
        }

        public void StopDockerContainers()
        {
            if (_dockerContainers == null || _dockerContainers.Count == 0)
            {
                return;
            }

            foreach (var containerId in _dockerContainers.ToList())
            {
                try
                {
                    var processStartInfo = new ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = $"stop {containerId}",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    };

                    using var process = Process.Start(processStartInfo);
                    process?.WaitForExit(TimeSpan.FromSeconds(10));
                }
                catch
                {
                    // Ignore errors during cleanup
                }
            }

            _dockerContainers.Clear();
        }
    }
}
