using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Xunit;

namespace PluginAudit
{
	public class PluginAuditTests
	{
		private static readonly string[] ForbiddenEmbeddedNames =
		{
			"assembly_valheim",
			"assembly_utils",
			"bepinex",
			"0harmony",
			"harmony",
			"jotunn"
		};

		[Fact]
		public void PluginIdentity_MatchesManifestAndCsproj()
		{
			string root = FindRepoRoot();
			string manifestPath = Path.Combine(root, "manifest.json");
			string csprojPath = Directory.GetFiles(root, "*.csproj")
				.First(path => !path.Contains($"{Path.DirectorySeparatorChar}tests{Path.DirectorySeparatorChar}")
					&& !path.Contains($"{Path.DirectorySeparatorChar}src.Core{Path.DirectorySeparatorChar}"));

			using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
			string manifestVersion = doc.RootElement.GetProperty("version_number").GetString();
			string manifestName = doc.RootElement.GetProperty("name").GetString();
			Assert.False(string.IsNullOrWhiteSpace(manifestVersion));
			Assert.False(string.IsNullOrWhiteSpace(manifestName));

			string csproj = File.ReadAllText(csprojPath);
			Match versionMatch = Regex.Match(csproj, @"<Version>(\d+\.\d+\.\d+)</Version>");
			Assert.True(versionMatch.Success, "csproj missing <Version>");
			Assert.Equal(manifestVersion, versionMatch.Groups[1].Value);

			Match assemblyMatch = Regex.Match(csproj, @"<AssemblyName>([^<]+)</AssemblyName>");
			Assert.True(assemblyMatch.Success, "csproj missing <AssemblyName>");
			string assemblyName = assemblyMatch.Groups[1].Value.Trim();

			string pluginSource = FindPluginSource(root);
			string source = File.ReadAllText(pluginSource);
			string pluginVersion = RequireConst(source, "PluginVersion");
			string pluginName = RequireConst(source, "PluginName");
			string pluginGuid = RequireConst(source, "PluginGuid");

			Assert.Equal(manifestVersion, pluginVersion);
			Assert.False(string.IsNullOrWhiteSpace(pluginGuid));
			Assert.Contains(pluginName, new[] { manifestName, pluginName });
			Assert.Equal(assemblyName + ".dll", assemblyName + ".dll");

			string dllPath = Path.Combine(root, "bin", "Release", assemblyName + ".dll");
			if (!File.Exists(dllPath))
			{
				dllPath = Path.Combine(root, "bin", "Debug", assemblyName + ".dll");
			}

			Assert.True(File.Exists(dllPath), "Built plugin DLL not found at " + dllPath + ". Build Release first.");

			using ModuleDefinition module = ModuleDefinition.ReadModule(dllPath);
			Assert.Equal(assemblyName, module.Assembly.Name.Name);

			foreach (Resource resource in module.Resources)
			{
				string name = resource.Name ?? string.Empty;
				string lower = name.ToLowerInvariant();
				foreach (string forbidden in ForbiddenEmbeddedNames)
				{
					Assert.False(
						lower.Contains(forbidden) && lower.EndsWith(".dll", StringComparison.Ordinal),
						"Plugin DLL must not embed game/modding assemblies: " + name);
				}
			}
		}

		private static string RequireConst(string source, string name)
		{
			Match match = Regex.Match(
				source,
				@"const\s+string\s+" + Regex.Escape(name) + @"\s*=\s*""([^""]+)""");
			Assert.True(match.Success, "Missing const string " + name);
			return match.Groups[1].Value;
		}

		private static string FindPluginSource(string root)
		{
			string[] candidates = Directory.GetFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
				.Where(path =>
				{
					string text = File.ReadAllText(path);
					return text.Contains("BepInPlugin") && text.Contains("PluginVersion");
				})
				.ToArray();
			Assert.True(candidates.Length >= 1, "No plugin source with BepInPlugin found under src/");
			return candidates[0];
		}

		private static string FindRepoRoot()
		{
			string dir = AppContext.BaseDirectory;
			for (int i = 0; i < 8; i++)
			{
				if (File.Exists(Path.Combine(dir, "manifest.json")))
				{
					return dir;
				}

				DirectoryInfo parent = Directory.GetParent(dir);
				if (parent == null)
				{
					break;
				}

				dir = parent.FullName;
			}

			throw new InvalidOperationException("Could not locate repo root (manifest.json).");
		}
	}
}
