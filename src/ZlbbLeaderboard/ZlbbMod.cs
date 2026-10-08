using System;
using Quintessential;

namespace ZlbbLeaderboard
{
	/// <summary>Entry point: shows zlbb.faendir.com data in the game's histograms (see docs/GAME_HOOKS.md).</summary>
	public class ZlbbMod : QuintessentialMod
	{
		public const string LogPrefix = "[ZlbbLeaderboard] ";

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
		}

		public override void Unload()
		{
		}
	}
}
