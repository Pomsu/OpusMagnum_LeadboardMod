using System;
using System.IO;
using System.Threading;
using Quintessential;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>Entry point: shows zlbb.faendir.com data in the game's histograms (see docs/GAME_HOOKS.md).</summary>
	public class ZlbbMod : QuintessentialMod
	{
		public const string LogPrefix = "[ZlbbLeaderboard] ";

		/// <summary>Set in PostLoad; histograms are served from its in-memory index and never touch the network.</summary>
		public static SyncService Sync { get; private set; }

		public static void Log(string text)
		{
			Logger.Log(LogPrefix + text);
		}

		public override void Load()
		{
			Log("loaded");
		}

		public override void PostLoad()
		{
			try
			{
				// same folder Quintessential uses for mod settings: Documents\My Games\Opus Magnum\<profile>\ModSettings
				string directory = Path.Combine(class_161.method_402(), "ModSettings", "ZlbbCache");
				string baseUrl = Environment.GetEnvironmentVariable("ZLBB_BASE_URL"); // test hook: point at a fake server
				Sync = new SyncService(
					new ZlbbClient(string.IsNullOrEmpty(baseUrl) ? ZlbbClient.DefaultBaseUrl : baseUrl),
					Path.Combine(directory, "om.snapshot"),
					null,
					Log);
				On.ScoreManager.method_1377 += HistogramPatch.Hook;
				On.class_140.method_321 += PanelPatch.Hook;
				SolveMetrics.Load(directory);
				On.class_257.ctor += SolveMetrics.Hook;
				StartStartupSync();
			}
			catch (Exception e)
			{
				Log("startup failed, leaderboard data disabled: " + e);
			}
		}

		public override void Unload()
		{
			On.ScoreManager.method_1377 -= HistogramPatch.Hook;
			On.class_140.method_321 -= PanelPatch.Hook;
			On.class_257.ctor -= SolveMetrics.Hook;
		}

		/// <summary>The one and only automatic sync: once per game start, on a background thread.</summary>
		private static void StartStartupSync()
		{
			var thread = new Thread(() =>
			{
				try
				{
					SyncOutcome outcome = Sync.RunOnce();
					Log("startup sync finished: " + outcome.State + (outcome.Error == null ? "" : " (" + outcome.Error + ")"));
				}
				catch (Exception e)
				{
					Log("startup sync crashed: " + e);
				}
			});
			thread.IsBackground = true;
			thread.Name = "ZlbbLeaderboard startup sync";
			thread.Start();
		}
	}
}
