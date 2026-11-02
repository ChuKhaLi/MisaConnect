using System;

namespace MisaConnect.EInvoice.Client;

public sealed class AssemblyMarker;

// Anchors metadata references to the shared core assemblies so the dual-surface
// parity rule (Principle VI) holds at the assembly level even before any
// Domain/Application type is consumed by name from this surface.
internal static class CoreAssemblyAnchors
{
    public static readonly Type Application = typeof(MisaConnect.EInvoice.Application.AssemblyMarker);
    public static readonly Type Domain = typeof(MisaConnect.EInvoice.Domain.AssemblyMarker);
}
