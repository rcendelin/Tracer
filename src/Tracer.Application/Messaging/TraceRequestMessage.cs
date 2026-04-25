// Message contracts are defined in Tracer.Contracts.Messages (the shared NuGet package).
// These global aliases make them available throughout Tracer.Application without
// requiring an explicit 'using Tracer.Contracts.Messages;' in every file.
global using TraceRequestMessage = Tracer.Contracts.Messages.TraceRequestMessage;
global using TraceResponseMessage = Tracer.Contracts.Messages.TraceResponseMessage;
global using ChangeEventMessage = Tracer.Contracts.Messages.ChangeEventMessage;
