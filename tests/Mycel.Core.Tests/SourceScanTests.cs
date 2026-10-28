using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Mycel.Core.Tests;

/// <summary>
/// Fails if the former product name appears anywhere in the repository (file contents or paths)
/// outside a small, explicit allowlist.
/// </summary>
public sealed class SourceScanTests
{
    // Built by concatenation so that this file does not contain the forbidden word itself.
    private static readonly string Forbidden = "topo" + "opt";

    private static readonly string[] Allowlist =
    {
        "docs/RENAME_PLAN.md",                                   // the rename plan documents the old name
        "src/Mycel.App/Diagnostics/CrashLogger.cs",              // legacy app-data folder migration
        "src/Mycel.App/Diagnostics/OrbitDiagnostics.cs",         // legacy environment variable fallback
        "tests/Mycel.Core.Tests/Legacy/LegacyFormatTests.cs",    // old-format fixtures
    };

    private static readonly string[] SkippedDirectories =
    {
        ".git", "bin", "obj", "dist", "runs", "artifacts", "TestResults", "publish", ".vs", ".idea",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Mycel.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Repository root (Mycel.sln) not found.");
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string current = stack.Pop();
            foreach (string sub in Directory.EnumerateDirectories(current))
            {
                string name = Path.GetFileName(sub);
                if (SkippedDirectories.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                stack.Push(sub);
            }
            foreach (string file in Directory.EnumerateFiles(current))
                yield return file;
        }
    }

    private static bool LooksBinary(string path)
    {
        using var fs = File.OpenRead(path);
        byte[] buffer = new byte[Math.Min(8192, fs.Length)];
        int read = fs.Read(buffer, 0, buffer.Length);
        for (int i = 0; i < read; i++)
            if (buffer[i] == 0) return true;
        return false;
    }

    [Fact]
    public void RepositoryContainsNoFormerProductName_OutsideAllowlist()
    {
        string root = FindRepoRoot();
        var offenders = new List<string>();

        foreach (string file in EnumerateFiles(root))
        {
            string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (Allowlist.Contains(rel, StringComparer.OrdinalIgnoreCase)) continue;
            if (file.EndsWith(".stl", StringComparison.OrdinalIgnoreCase)) continue;

            if (rel.Contains(Forbidden, StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add(rel + " (path)");
                continue;
            }

            if (LooksBinary(file)) continue;
            if (File.ReadAllText(file).Contains(Forbidden, StringComparison.OrdinalIgnoreCase))
                offenders.Add(rel);
        }

        Assert.True(offenders.Count == 0,
            "Former product name found outside the allowlist in:\n" + string.Join("\n", offenders));
    }
}

