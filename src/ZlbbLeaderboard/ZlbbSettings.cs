using Quintessential.Settings;
using YamlDotNet.Serialization;

namespace ZlbbLeaderboard
{
	/// <summary>
	/// Shown on Quintessential's Mods screen (only checkboxes, buttons and keybindings are drawn there). Saved as
	/// Documents\My Games\Opus Magnum\...\ModSettings\ZlbbLeaderboard.yaml - the author name is stored there too, but is set through its own screen.
	/// </summary>
	public class ZlbbSettings
	{
		[SettingsLabel("Show zlbb leaderboard data in the histograms")]
		public bool Enabled = true;

		[SettingsLabel("Submit solves without asking after recording a GIF")]
		public bool AutoSubmit = false;

		/// <summary>The name solutions are published under on zlbb. Empty = submitting is off.</summary>
		public string AuthorName = "";

		[YamlIgnore]
		[SettingsLabel("Set author name (needed for submitting)")]
		public SettingsButton SetAuthorName = ZlbbMod.OpenAuthorNameScreen;

		[YamlIgnore]
		[SettingsLabel("Sync with zlbb now")]
		public SettingsButton SyncNow = ZlbbMod.StartManualSync;
	}
}
