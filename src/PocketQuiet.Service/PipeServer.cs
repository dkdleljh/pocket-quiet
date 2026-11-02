using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PocketQuiet.Control;
using PocketQuiet.Core;

namespace PocketQuiet.Service;

sealed class PipeServer(ControlSession session, IEventSink log)
{
    public async Task Run(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                var acl = new PipeSecurity();
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
                acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
                using var pipe = NamedPipeServerStreamAcl.Create(Wire.Pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous, 8192, 8192, acl);
                await pipe.WaitForConnectionAsync(stop);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
                timeout.CancelAfter(2500);
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
                Response response;
                try
                {
                    string line = await LineProtocol.ReadAsync(reader, timeout.Token);
                    var request = JsonSerializer.Deserialize<Request>(line, Wire.Json) ?? throw new ArgumentException("잘못된 요청");
                    timeout.Token.ThrowIfCancellationRequested();
                    response = session.Handle(request);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { response = new(false, session.Snapshot, ex.Message); }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response, Wire.Json).AsMemory(), timeout.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                log.Error("IPC: " + ex.Message);
                try { await Task.Delay(500, stop); } catch (OperationCanceledException) { }
            }
        }
    }
}
