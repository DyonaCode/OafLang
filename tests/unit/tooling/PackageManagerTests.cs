using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Oaf.Tests.Framework;
using Oaf.Tooling.PackageManagement;

namespace Oaf.Tests.Unit.Tooling;

public static class PackageManagerTests
{
    public static IReadOnlyList<(string Name, Action Test)> GetTests()
    {
        return
        [
            ("parses_dependency_spec", ParsesDependencySpec),
            ("installs_manifest_dependencies", InstallsManifestDependencies),
            ("writes_deterministic_lock_file", WritesDeterministicLockFile),
            ("verify_detects_tampered_metadata", VerifyDetectsTamperedMetadata),
            ("installs_from_local_source_index", InstallsFromLocalSourceIndex),
            ("install_fails_when_source_missing_dependency", InstallFailsWhenSourceMissingDependency),
            ("resolves_transitive_dependencies_with_ranges", ResolvesTransitiveDependenciesWithRanges),
            ("install_fails_when_transitive_constraints_conflict", InstallFailsWhenTransitiveConstraintsConflict),
            ("compose_source_loads_only_imported_modules", ComposeSourceLoadsOnlyImportedModules),
            ("compose_source_resolves_transitive_module_imports", ComposeSourceResolvesTransitiveModuleImports),
            ("compose_source_fails_when_module_path_does_not_match_declaration", ComposeSourceFailsWhenModulePathDoesNotMatchDeclaration),
            ("compose_source_loads_local_modules_by_folder_path", ComposeSourceLoadsLocalModulesByFolderPath),
            ("compose_source_infers_module_declaration_when_missing", ComposeSourceInfersModuleDeclarationWhenMissing)
        ];
    }

    private static void ParsesDependencySpec()
    {
        TestAssertions.True(OafPackageManager.TryParseDependencySpec("core.math@1.2.3", out var dependency));
        TestAssertions.Equal("core.math", dependency.Name);
        TestAssertions.Equal("1.2.3", dependency.Version);

        TestAssertions.False(OafPackageManager.TryParseDependencySpec("missingVersion@", out _));
        TestAssertions.False(OafPackageManager.TryParseDependencySpec("@1.0.0", out _));
        TestAssertions.False(OafPackageManager.TryParseDependencySpec("bad name@1.0.0", out _));
    }

    private static void InstallsManifestDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "packages.txt");

            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@1.0.0", out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "beta@2.1.0", out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@1.0.1", out _));
            TestAssertions.True(OafPackageManager.RemoveDependency(manifestPath, "beta", out _));
            TestAssertions.True(OafPackageManager.Install(manifestPath, out _));

            var manifestText = File.ReadAllText(manifestPath);
            TestAssertions.True(manifestText.Contains("alpha@1.0.1", StringComparison.Ordinal));
            TestAssertions.False(manifestText.Contains("beta@2.1.0", StringComparison.Ordinal));

            var lockPath = Path.Combine(root, "packages.lock");
            TestAssertions.True(File.Exists(lockPath), "Expected lock file to be generated.");
            var lockText = File.ReadAllText(lockPath);
            TestAssertions.True(lockText.Contains("alpha@1.0.1", StringComparison.Ordinal));
            TestAssertions.True(lockText.Contains("source=manifest", StringComparison.Ordinal));
            TestAssertions.True(lockText.Contains("manifest_sha256=", StringComparison.Ordinal));

            var metadataPath = Path.Combine(root, ".oaf", "packages", "alpha", "1.0.1", "package.meta");
            TestAssertions.True(File.Exists(metadataPath), "Expected package metadata file.");
            var metadata = File.ReadAllText(metadataPath);
            TestAssertions.True(metadata.Contains("source=manifest", StringComparison.Ordinal));
            TestAssertions.True(metadata.Contains("hash=", StringComparison.Ordinal));

            TestAssertions.True(OafPackageManager.VerifyInstall(manifestPath, out _), "Expected package verification to succeed.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void VerifyDetectsTamperedMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_verify_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "packages.txt");
            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@1.0.0", out _));
            TestAssertions.True(OafPackageManager.Install(manifestPath, out _));
            TestAssertions.True(OafPackageManager.VerifyInstall(manifestPath, out _), "Expected verification to pass before tampering.");

            var metadataPath = Path.Combine(root, ".oaf", "packages", "alpha", "1.0.0", "package.meta");
            var metadata = File.ReadAllText(metadataPath);
            var tampered = metadata.Replace("source=manifest", "source=tampered", StringComparison.Ordinal);
            File.WriteAllText(metadataPath, tampered);

            TestAssertions.False(OafPackageManager.VerifyInstall(manifestPath, out _), "Expected verification to fail after tampering.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void WritesDeterministicLockFile()
    {
        var firstRoot = Path.Combine(Path.GetTempPath(), $"oaf_pkg_lock_a_{Guid.NewGuid():N}");
        var secondRoot = Path.Combine(Path.GetTempPath(), $"oaf_pkg_lock_b_{Guid.NewGuid():N}");
        Directory.CreateDirectory(firstRoot);
        Directory.CreateDirectory(secondRoot);

        try
        {
            var firstManifestPath = Path.Combine(firstRoot, "packages.txt");
            var secondManifestPath = Path.Combine(secondRoot, "packages.txt");

            TestAssertions.True(OafPackageManager.InitManifest(firstManifestPath, out _));
            TestAssertions.True(OafPackageManager.InitManifest(secondManifestPath, out _));

            TestAssertions.True(OafPackageManager.AddDependency(firstManifestPath, "gamma@3.2.1", out _));
            TestAssertions.True(OafPackageManager.AddDependency(firstManifestPath, "alpha@1.0.0", out _));
            TestAssertions.True(OafPackageManager.AddDependency(firstManifestPath, "beta@2.0.5", out _));

            TestAssertions.True(OafPackageManager.AddDependency(secondManifestPath, "beta@2.0.5", out _));
            TestAssertions.True(OafPackageManager.AddDependency(secondManifestPath, "gamma@3.2.1", out _));
            TestAssertions.True(OafPackageManager.AddDependency(secondManifestPath, "alpha@1.0.0", out _));

            TestAssertions.True(OafPackageManager.Install(firstManifestPath, out _));
            TestAssertions.True(OafPackageManager.Install(secondManifestPath, out _));

            var firstLockPath = Path.Combine(firstRoot, "packages.lock");
            var secondLockPath = Path.Combine(secondRoot, "packages.lock");

            TestAssertions.True(File.Exists(firstLockPath), "Expected first lock file.");
            TestAssertions.True(File.Exists(secondLockPath), "Expected second lock file.");

            var firstLockNormalized = NormalizeLineEndings(File.ReadAllText(firstLockPath));
            var secondLockNormalized = NormalizeLineEndings(File.ReadAllText(secondLockPath));
            TestAssertions.Equal(firstLockNormalized, secondLockNormalized, "Expected lock files to be deterministic.");
        }
        finally
        {
            if (Directory.Exists(firstRoot))
            {
                Directory.Delete(firstRoot, recursive: true);
            }

            if (Directory.Exists(secondRoot))
            {
                Directory.Delete(secondRoot, recursive: true);
            }
        }
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static void InstallsFromLocalSourceIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_source_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var registryDir = Path.Combine(root, "registry");
            Directory.CreateDirectory(registryDir);

            var artifactPath = Path.Combine(registryDir, "alpha-1.0.0.oafpkg");
            using (var archive = ZipFile.Open(artifactPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("README.txt");
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes("alpha package");
                stream.Write(bytes, 0, bytes.Length);
            }

            var artifactHash = ComputeFileSha256Hex(artifactPath);
            var sourceIndexPath = Path.Combine(registryDir, "index.json");
            File.WriteAllText(
                sourceIndexPath,
                $$"""
                {
                  "source": "localtest",
                  "packages": [
                    {
                      "name": "alpha",
                      "version": "1.0.0",
                      "artifact": "./alpha-1.0.0.oafpkg",
                      "sha256": "{{artifactHash}}"
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, OafPackageManager.DefaultSourcesFileName), sourceIndexPath + Environment.NewLine);

            var manifestPath = Path.Combine(root, OafPackageManager.DefaultManifestFileName);
            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@1.0.0", out _));

            TestAssertions.True(OafPackageManager.Install(manifestPath, out _), "Expected source-backed install to succeed.");
            TestAssertions.True(OafPackageManager.VerifyInstall(manifestPath, out _), "Expected verification to pass.");

            var packageDir = Path.Combine(root, ".oaf", "packages", "alpha", "1.0.0");
            var localArtifact = Path.Combine(packageDir, "alpha-1.0.0.oafpkg");
            var extractedFile = Path.Combine(packageDir, "content", "README.txt");
            TestAssertions.True(File.Exists(localArtifact), "Expected copied artifact in package directory.");
            TestAssertions.True(File.Exists(extractedFile), "Expected extracted artifact content.");

            var lockText = File.ReadAllText(Path.Combine(root, OafPackageManager.DefaultLockFileName));
            TestAssertions.True(lockText.Contains("source=localtest", StringComparison.Ordinal));
            TestAssertions.True(lockText.Contains($"artifact_sha256={artifactHash}", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void InstallFailsWhenSourceMissingDependency()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_source_missing_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var registryDir = Path.Combine(root, "registry");
            Directory.CreateDirectory(registryDir);

            var artifactPath = Path.Combine(registryDir, "alpha-1.0.0.oafpkg");
            using (var archive = ZipFile.Open(artifactPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("README.txt");
                using var stream = entry.Open();
                var bytes = Encoding.UTF8.GetBytes("alpha package");
                stream.Write(bytes, 0, bytes.Length);
            }

            var artifactHash = ComputeFileSha256Hex(artifactPath);
            var sourceIndexPath = Path.Combine(registryDir, "index.json");
            File.WriteAllText(
                sourceIndexPath,
                $$"""
                {
                  "source": "localtest",
                  "packages": [
                    {
                      "name": "alpha",
                      "version": "1.0.0",
                      "artifact": "./alpha-1.0.0.oafpkg",
                      "sha256": "{{artifactHash}}"
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, OafPackageManager.DefaultSourcesFileName), sourceIndexPath + Environment.NewLine);

            var manifestPath = Path.Combine(root, OafPackageManager.DefaultManifestFileName);
            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "beta@1.0.0", out _));

            TestAssertions.False(OafPackageManager.Install(manifestPath, out _), "Expected install to fail for unresolved source dependency.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string ComputeFileSha256Hex(string filePath)
    {
        var hash = SHA256.HashData(File.ReadAllBytes(filePath));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static void ResolvesTransitiveDependenciesWithRanges()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_transitive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var registryDir = Path.Combine(root, "registry");
            Directory.CreateDirectory(registryDir);

            var alphaArtifact = CreatePackageArtifact(registryDir, "alpha-1.2.0");
            var betaArtifact = CreatePackageArtifact(registryDir, "beta-2.1.0");
            var betaOldArtifact = CreatePackageArtifact(registryDir, "beta-1.9.0");

            var sourceIndexPath = Path.Combine(registryDir, "index.json");
            File.WriteAllText(
                sourceIndexPath,
                $$"""
                {
                  "source": "localtest",
                  "packages": [
                    {
                      "name": "alpha",
                      "version": "1.2.0",
                      "artifact": "./alpha-1.2.0.oafpkg",
                      "sha256": "{{ComputeFileSha256Hex(alphaArtifact)}}",
                      "dependencies": ["beta@^2.0.0"]
                    },
                    {
                      "name": "beta",
                      "version": "2.1.0",
                      "artifact": "./beta-2.1.0.oafpkg",
                      "sha256": "{{ComputeFileSha256Hex(betaArtifact)}}"
                    },
                    {
                      "name": "beta",
                      "version": "1.9.0",
                      "artifact": "./beta-1.9.0.oafpkg",
                      "sha256": "{{ComputeFileSha256Hex(betaOldArtifact)}}"
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, OafPackageManager.DefaultSourcesFileName), sourceIndexPath + Environment.NewLine);

            var manifestPath = Path.Combine(root, OafPackageManager.DefaultManifestFileName);
            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@>=1.0.0 <2.0.0", out _));

            TestAssertions.True(OafPackageManager.Install(manifestPath, out _), "Expected install with transitive dependencies to succeed.");
            TestAssertions.True(OafPackageManager.VerifyInstall(manifestPath, out _), "Expected verification to pass.");

            var lockText = File.ReadAllText(Path.Combine(root, OafPackageManager.DefaultLockFileName));
            TestAssertions.True(lockText.Contains("alpha@1.2.0", StringComparison.Ordinal));
            TestAssertions.True(lockText.Contains("beta@2.1.0", StringComparison.Ordinal));
            TestAssertions.False(lockText.Contains("beta@1.9.0", StringComparison.Ordinal));

            var transitivePackageDir = Path.Combine(root, ".oaf", "packages", "beta", "2.1.0");
            TestAssertions.True(Directory.Exists(transitivePackageDir), "Expected transitive package directory.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void InstallFailsWhenTransitiveConstraintsConflict()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_conflict_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var registryDir = Path.Combine(root, "registry");
            Directory.CreateDirectory(registryDir);

            var alphaArtifact = CreatePackageArtifact(registryDir, "alpha-1.0.0");
            var gammaArtifact = CreatePackageArtifact(registryDir, "gamma-1.0.0");
            var betaV1Artifact = CreatePackageArtifact(registryDir, "beta-1.2.0");
            var betaV2Artifact = CreatePackageArtifact(registryDir, "beta-2.0.1");

            var sourceIndexPath = Path.Combine(registryDir, "index.json");
            File.WriteAllText(
                sourceIndexPath,
                $$"""
                {
                  "source": "localtest",
                  "packages": [
                    {
                      "name": "alpha",
                      "version": "1.0.0",
                      "artifact": "./alpha-1.0.0.oafpkg",
                      "sha256": "{{ComputeFileSha256Hex(alphaArtifact)}}",
                      "dependencies": ["beta@^1.0.0"]
                    },
                    {
                      "name": "gamma",
                      "version": "1.0.0",
                      "artifact": "./gamma-1.0.0.oafpkg",
                      "sha256": "{{ComputeFileSha256Hex(gammaArtifact)}}",
                      "dependencies": ["beta@^2.0.0"]
                    },
                    {
                      "name": "beta",
                      "version": "1.2.0",
                      "artifact": "./beta-1.2.0.oafpkg",
                      "sha256": "{{ComputeFileSha256Hex(betaV1Artifact)}}"
                    },
                    {
                      "name": "beta",
                      "version": "2.0.1",
                      "artifact": "./beta-2.0.1.oafpkg",
                      "sha256": "{{ComputeFileSha256Hex(betaV2Artifact)}}"
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, OafPackageManager.DefaultSourcesFileName), sourceIndexPath + Environment.NewLine);

            var manifestPath = Path.Combine(root, OafPackageManager.DefaultManifestFileName);
            TestAssertions.True(OafPackageManager.InitManifest(manifestPath, out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "alpha@1.0.0", out _));
            TestAssertions.True(OafPackageManager.AddDependency(manifestPath, "gamma@1.0.0", out _));

            TestAssertions.False(
                OafPackageManager.Install(manifestPath, out _),
                "Expected install to fail for incompatible transitive constraints.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreatePackageArtifact(string directory, string name)
    {
        var path = Path.Combine(directory, $"{name}.oafpkg");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        var entry = archive.CreateEntry("README.txt");
        using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(name);
        stream.Write(bytes, 0, bytes.Length);
        return path;
    }

    private static void ComposeSourceLoadsOnlyImportedModules()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_compose_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var packageRoot = Path.Combine(root, ".oaf", "packages");
            var alphaDir = Path.Combine(packageRoot, "alpha", "1.0.0", "content", "pkg");
            var betaDir = Path.Combine(packageRoot, "beta", "1.0.0", "content", "pkg");
            Directory.CreateDirectory(alphaDir);
            Directory.CreateDirectory(betaDir);

            File.WriteAllText(Path.Combine(alphaDir, "alpha.oaf"), "module pkg.alpha; flux alphaValue = 1;");
            File.WriteAllText(Path.Combine(betaDir, "beta.oaf"), "module pkg.beta; flux betaValue = 2;");

            File.WriteAllText(
                Path.Combine(root, OafPackageManager.DefaultLockFileName),
                """
                # Oaf package lock
                format=2
                dependency_count=2
                manifest_sha256=0000000000000000000000000000000000000000000000000000000000000000
                alpha@1.0.0 source=local sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa artifact_sha256=none
                beta@1.0.0 source=local sha256=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb artifact_sha256=none
                """);

            const string entrySource = "import pkg.alpha; return pkg.alpha.alphaValue;";
            TestAssertions.True(
                OafPackageManager.TryComposeCompilationSource(entrySource, root, out var composedSource, out _),
                "Expected composition to succeed.");

            TestAssertions.True(composedSource.Contains("module pkg.alpha;", StringComparison.Ordinal));
            TestAssertions.False(composedSource.Contains("module pkg.beta;", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ComposeSourceResolvesTransitiveModuleImports()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_compose_transitive_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var packageRoot = Path.Combine(root, ".oaf", "packages");
            var alphaDir = Path.Combine(packageRoot, "alpha", "1.0.0", "content", "pkg");
            var betaDir = Path.Combine(packageRoot, "beta", "1.0.0", "content", "pkg");
            Directory.CreateDirectory(alphaDir);
            Directory.CreateDirectory(betaDir);

            File.WriteAllText(Path.Combine(alphaDir, "alpha.oaf"), "module pkg.alpha; import pkg.beta; flux alphaValue = pkg.beta.betaValue;");
            File.WriteAllText(Path.Combine(betaDir, "beta.oaf"), "module pkg.beta; flux betaValue = 2;");

            File.WriteAllText(
                Path.Combine(root, OafPackageManager.DefaultLockFileName),
                """
                # Oaf package lock
                format=2
                dependency_count=2
                manifest_sha256=0000000000000000000000000000000000000000000000000000000000000000
                alpha@1.0.0 source=local sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa artifact_sha256=none
                beta@1.0.0 source=local sha256=bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb artifact_sha256=none
                """);

            const string entrySource = "import pkg.alpha; return pkg.alpha.alphaValue;";
            TestAssertions.True(
                OafPackageManager.TryComposeCompilationSource(entrySource, root, out var composedSource, out _),
                "Expected composition to succeed.");

            var alphaIndex = composedSource.IndexOf("module pkg.alpha;", StringComparison.Ordinal);
            var betaIndex = composedSource.IndexOf("module pkg.beta;", StringComparison.Ordinal);
            TestAssertions.True(alphaIndex >= 0 && betaIndex >= 0, "Expected both modules in composed source.");
            TestAssertions.True(betaIndex < alphaIndex, "Expected transitive dependency module to be composed before dependent module.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ComposeSourceFailsWhenModulePathDoesNotMatchDeclaration()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_compose_path_mismatch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var moduleDir = Path.Combine(root, ".oaf", "packages", "alpha", "1.0.0", "content", "pkg");
            Directory.CreateDirectory(moduleDir);
            File.WriteAllText(Path.Combine(moduleDir, "math.oaf"), "module pkg.calc; flux value = 1;");

            File.WriteAllText(
                Path.Combine(root, OafPackageManager.DefaultLockFileName),
                """
                # Oaf package lock
                format=2
                dependency_count=1
                manifest_sha256=0000000000000000000000000000000000000000000000000000000000000000
                alpha@1.0.0 source=local sha256=aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa artifact_sha256=none
                """);

            const string entrySource = "import pkg.math; return pkg.math.value;";
            TestAssertions.False(
                OafPackageManager.TryComposeCompilationSource(entrySource, root, out _, out var message),
                "Expected composition to fail when module declaration does not match file path.");
            TestAssertions.True(
                message.Contains("must match file path module 'pkg.math'", StringComparison.Ordinal),
                "Expected mismatch error message.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ComposeSourceLoadsLocalModulesByFolderPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_compose_local_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var clientDir = Path.Combine(root, "net", "http", "client");
            var typesDir = Path.Combine(root, "net", "http", "types");
            Directory.CreateDirectory(clientDir);
            Directory.CreateDirectory(typesDir);

            File.WriteAllText(
                Path.Combine(typesDir, "status.oaf"),
                "module net.http.types.status; flux ok = 200;");
            File.WriteAllText(
                Path.Combine(clientDir, "core.oaf"),
                "module net.http.client.core; import net.http.types.status; flux clientDefault = net.http.types.status.ok;");

            const string entrySource = "import net.http.client.core; return net.http.client.core.clientDefault;";
            TestAssertions.True(
                OafPackageManager.TryComposeCompilationSource(entrySource, root, out var composedSource, out _),
                "Expected local-folder module composition to succeed.");

            TestAssertions.True(composedSource.Contains("module net.http.types.status;", StringComparison.Ordinal));
            TestAssertions.True(composedSource.Contains("module net.http.client.core;", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static void ComposeSourceInfersModuleDeclarationWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"oaf_pkg_compose_infer_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var httpDir = Path.Combine(root, "net", "http");
            Directory.CreateDirectory(httpDir);
            File.WriteAllText(Path.Combine(httpDir, "helpers.oaf"), "flux helper_value = 7;");

            const string entrySource = "import net.http.helpers; return net.http.helpers.helper_value;";
            TestAssertions.True(
                OafPackageManager.TryComposeCompilationSource(entrySource, root, out var composedSource, out _),
                "Expected local module composition to infer module declaration.");

            TestAssertions.True(
                composedSource.Contains("module net.http.helpers;", StringComparison.Ordinal),
                "Expected inferred module declaration to be inserted.");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
