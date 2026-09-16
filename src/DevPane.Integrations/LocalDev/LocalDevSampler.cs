using System.Diagnostics;

namespace DevPane.Integrations.LocalDev;

/// <summary>A dev server listening on this machine.</summary>
/// <param name="IsExposed">True when other devices on the network may reach it.</param>
public sealed record DevServer(int Port, string Name, bool IsExposed);

/// <summary>Running processes of one developer tool, such as all node processes.</summary>
public sealed record DevProcessGroup(string Name, int Count, ulong MemoryBytes);

/// <param name="VirtualMachineMemoryBytes">Memory used by the WSL 2 or Docker virtual machine, or null when none is running.</param>
public sealed record LocalDevSnapshot(
    IReadOnlyList<DevServer> Servers,
    IReadOnlyList<DevProcessGroup> Processes,
    ulong? VirtualMachineMemoryBytes);

/// <summary>
/// Finds dev servers, memory used by developer tools, and WSL/Docker VM memory, from one process snapshot.
/// </summary>
public static class LocalDevSampler
{
    public static LocalDevSnapshot Sample()
    {
        // Process details below come from this one system snapshot; no process is opened individually.
        Process[] processes = Process.GetProcesses();
        try
        {
            var names = new Dictionary<int, string>(processes.Length);
            var groups = new Dictionary<string, (int Count, ulong Memory)>(StringComparer.Ordinal);
            ulong? vmMemory = null;

            foreach (var process in processes)
            {
                string name = process.ProcessName;
                names[process.Id] = name;

                if (DevCatalog.IsVirtualMachine(name))
                {
                    // The VM's memory shows up as this process's working set.
                    vmMemory = (vmMemory ?? 0) + (ulong)process.WorkingSet64;
                }
                else if (DevCatalog.GroupOf(name) is { } group)
                {
                    var (count, memory) = groups.GetValueOrDefault(group);
                    groups[group] = (count + 1, memory + (ulong)process.PrivateMemorySize64);
                }
            }

            var servers = TcpListeners.Read()
                .Select(listener => (Listener: listener,
                    Label: names.TryGetValue(listener.ProcessId, out string? owner) ? DevCatalog.ServerLabel(owner) : null))
                .Where(entry => entry.Label is not null)
                .GroupBy(entry => entry.Listener.Port) // IPv4 and IPv6 listeners on the same port count once
                .Select(port => new DevServer(port.Key, port.First().Label!, port.Any(entry => entry.Listener.IsExposed)))
                .OrderBy(server => server.Port)
                .ToList();

            var processGroups = groups
                .Select(group => new DevProcessGroup(group.Key, group.Value.Count, group.Value.Memory))
                .OrderByDescending(group => group.MemoryBytes)
                .ToList();

            return new LocalDevSnapshot(servers, processGroups, vmMemory);
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
