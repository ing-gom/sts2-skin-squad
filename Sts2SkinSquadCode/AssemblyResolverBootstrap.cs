using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace Sts2SkinSquad;

/// <summary>
/// STS2's ModManager loads only {ModId}.dll and does NOT add the mod folder to the runtime
/// probing paths, so the sibling Sts2.ModKit.dll is never found by default. Register an
/// AssemblyLoadContext.Resolving handler (via [ModuleInitializer], which runs before any of our
/// methods JIT) that looks next to our own DLL. See [[feedback_modkit_consumer_rebuild]].
///
/// ★Without this the mod loads only by luck: every sister mod ships the same handler, and once any
/// of them is loaded first its handler resolves Sts2.ModKit for us too. Whenever the game's load
/// order happens to put Skin Squad ahead of every other ModKit consumer, MainFile.Initialize dies
/// at JIT time with FileNotFoundException('Sts2.ModKit') and the whole mod is silently inert.
/// </summary>
internal static class AssemblyResolverBootstrap
{
    private static bool _registered;

    [ModuleInitializer]
    internal static void Register()
    {
        if (_registered) return;
        _registered = true;

        var self = typeof(AssemblyResolverBootstrap).Assembly;
        var ctx = AssemblyLoadContext.GetLoadContext(self);
        if (ctx is null) return;

        var modDir = Path.GetDirectoryName(self.Location);
        if (string.IsNullOrEmpty(modDir)) return;

        ctx.Resolving += (loadContext, name) =>
        {
            if (string.IsNullOrEmpty(name.Name)) return null;
            var candidate = Path.Combine(modDir, name.Name + ".dll");
            return File.Exists(candidate) ? loadContext.LoadFromAssemblyPath(candidate) : null;
        };
    }
}
