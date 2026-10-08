using System;
using System.IO;
using System.Linq;
using System.Threading;
using Quintessential;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>Entry point: shows zlbb.faendir.com data in the game's histograms and can submit solves (see docs/GAME_HOOKS.md).</summary>
	public class ZlbbMod : QuintessentialMod
	{
		public const string LogPrefix = "[ZlbbLeaderboard] ";

		public static ZlbbMod Instance { get; private set; }

		/// <summary>Set in PostLoad; histograms are served from its in-memory index and never touch the network.</summary>
		public static SyncService Sync { get; private set; }

		/// <summary>Used for submissions only. Normally zlbb itself; the ZLBB_SUBMIT_URL environment variable points it at a local test server.</summary>
		public static ZlbbSubmitter Submitter { get; private set; }

		public override Type SettingsType
		{
			get { return typeof(ZlbbSettings); }
		}

		public static ZlbbSettings Config
		{
			get { return (Instance == null ? null : Instance.Settings as ZlbbSettings) ?? new ZlbbSettings(); }
		}

		public static bool Enabled
		{
			get { return Config.Enabled; }
		}

		private static SyncOutcome lastManualOutcome;

		public ZlbbMod()
		{
			Settings = new ZlbbSettings();
			Instance = this;
		}

		/// <summary>Where submissions go (shown in the confirmation window).</summary>
		public static string SubmitHost { get; private set; }

		public static void Log(string text)
		{
			try
			{
				Logger.Log(LogPrefix + text);
			}
			catch (Exception)
			{
				// logging must never be able to break a hook or a background thread
			}
		}

		private static string ShortHash(string text)
		{
			uint hash = 2166136261;
			foreach (char c in text)
				hash = (hash ^ c) * 16777619;
			return hash.ToString("x8");
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
				string baseUrl = Environment.GetEnvironmentVariable("ZLBB_BASE_URL"); // test hook: point the data sync at a fake server
				bool customBase = !string.IsNullOrEmpty(baseUrl);
				// a test server must never share (and pollute) the real cache: its snapshot gets its own file
				string snapshotName = customBase ? "om-" + ShortHash(baseUrl) + ".snapshot" : "om.snapshot";
				Sync = new SyncService(
					new ZlbbClient(customBase ? baseUrl : ZlbbClient.DefaultBaseUrl),
					Path.Combine(directory, snapshotName),
					null,
					Log) { PreferServerClock = !customBase };
				if (customBase)
					Log("data sync goes to " + baseUrl + " instead of zlbb (ZLBB_BASE_URL), using its own cache file");

				string submitUrl = Environment.GetEnvironmentVariable("ZLBB_SUBMIT_URL"); // test hook: point submissions at a local test server
				string submitBase = string.IsNullOrEmpty(submitUrl) ? ZlbbClient.DefaultBaseUrl : submitUrl;
				Submitter = new ZlbbSubmitter(new ZlbbClient(submitBase));
				SubmitHost = new Uri(submitBase).Host;
				if (!string.IsNullOrEmpty(submitUrl))
					Log("submissions go to " + submitUrl + " instead of zlbb (ZLBB_SUBMIT_URL)");

				On.ScoreManager.method_1377 += HistogramPatch.Hook;
				On.class_140.method_321 += PanelPatch.Hook;
				SolveMetrics.Load(directory);
				On.class_257.ctor += SolveMetrics.Hook;
				On.class_250.ctor += GifSubmitHook.RecorderCtor;
				On.class_92.method_77 += GifSubmitHook.Finish;
				On.class_92.method_78 += GifSubmitHook.Cancel;
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
			On.class_250.ctor -= GifSubmitHook.RecorderCtor;
			On.class_92.method_77 -= GifSubmitHook.Finish;
			On.class_92.method_78 -= GifSubmitHook.Cancel;
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

		// ---- settings buttons ----

		public static void OpenAuthorNameScreen()
		{
			UI.OpenScreen(new AuthorNameScreen());
		}

		/// <summary>"Sync with zlbb now": one diff request (60 s cooldown), result shown in a small window.</summary>
		public static void StartManualSync()
		{
			lastManualOutcome = null;
			var thread = new Thread(() =>
			{
				try
				{
					lastManualOutcome = Sync == null ? new SyncOutcome { State = SyncState.Failed, Error = "the mod did not start" } : Sync.SyncNow();
				}
				catch (Exception e)
				{
					lastManualOutcome = new SyncOutcome { State = SyncState.Failed, Error = e.Message };
				}
			});
			thread.IsBackground = true;
			thread.Name = "ZlbbLeaderboard manual sync";
			thread.Start();
			UI.OpenScreen(new SyncStatusScreen(() => lastManualOutcome));
		}

		/// <summary>"zlbb data status": what is stored, how old it is. Makes no request.</summary>
		public static void ShowDataStatus()
		{
			UI.OpenScreen(new DataStatusScreen());
		}

		/// <summary>"Re-download zlbb data": asks first, because it fetches the whole ~22 MB history again.</summary>
		public static void AskRedownload()
		{
			UI.OpenScreen(new ConfirmScreen(
				"Re-download zlbb data?",
				new[]
				{
					"This fetches the whole leaderboard history again (about 22 MB) and replaces the saved copy.",
					"The old data stays in use if the download fails.",
				},
				"RE-DOWNLOAD",
				StartRedownload));
		}

		private static void StartRedownload()
		{
			lastManualOutcome = null;
			var thread = new Thread(() =>
			{
				try
				{
					lastManualOutcome = Sync == null ? new SyncOutcome { State = SyncState.Failed, Error = "the mod did not start" } : Sync.RedownloadAll();
				}
				catch (Exception e)
				{
					lastManualOutcome = new SyncOutcome { State = SyncState.Failed, Error = e.Message };
				}
			});
			thread.IsBackground = true;
			thread.Name = "ZlbbLeaderboard re-download";
			thread.Start();
			UI.OpenScreen(new SyncStatusScreen(() => lastManualOutcome));
		}

		/// <summary>Saves the settings file after a change made outside the Mods screen (the author name). Uses Quintessential's own (internal) saver.</summary>
		public static void SaveConfig()
		{
			if (Instance == null)
				return;
			try
			{
				Type modsScreen = typeof(QuintessentialMod).Assembly.GetType("Quintessential.ModsScreen");
				System.Reflection.MethodInfo save = modsScreen == null ? null : modsScreen.GetMethod("SaveSettings", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
				if (save == null)
					throw new MissingMethodException("Quintessential.ModsScreen.SaveSettings");
				save.Invoke(null, new object[] { Instance });
			}
			catch (Exception e)
			{
				Log("could not save the settings: " + e.Message);
			}
		}
	}
}
