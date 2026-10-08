using System;
using System.IO;
using System.Reflection;
using Quintessential;

namespace ZlbbLeaderboard
{
	/// <summary>
	/// Mirrors what the current game version does for zlbb: when a GIF has been recorded, offer to submit the solution with it.
	/// The old game build Quintessential runs on lacks that feature. Cancelled recordings (class_92.method_78) are ignored, and nothing
	/// is sent unless the author name is set and the player confirms in the window; there is deliberately no automatic submitting.
	/// </summary>
	internal static class GifSubmitHook
	{
		private const long MaxGifBytes = 25L * 1024 * 1024;

		private static readonly FieldInfo GifStream = typeof(class_92).GetField("field_922", BindingFlags.Instance | BindingFlags.NonPublic);
		[ThreadStatic] private static bool cancelling;
		private static readonly FieldInfo RecorderEditor = typeof(class_250).GetField("field_2022", BindingFlags.Instance | BindingFlags.NonPublic);
		private static Solution recordedSolution;
		private static bool nameHintShown;

		/// <summary>
		/// The recorder screen is created with a solution and replays it in its own editor (field_2022); the GIF shows what that editor
		/// holds, so that is the solution to submit. The constructor argument is the fallback.
		/// </summary>
		public static void RecorderCtor(On.class_250.orig_ctor orig, class_250 self, Solution solution)
		{
			orig(self, solution);
			Solution owned = null;
			try
			{
				var editor = RecorderEditor == null ? null : RecorderEditor.GetValue(self) as SolutionEditorBase;
				owned = editor == null ? null : editor.method_502();
			}
			catch (Exception e)
			{
				ZlbbMod.Log("could not read the recorder's solution: " + e.Message);
			}
			recordedSolution = owned ?? solution;
		}

		/// <summary>method_78 finishes the file and then deletes it; that is a cancel, not a recording.</summary>
		public static void Cancel(On.class_92.orig_method_78 orig, class_92 self)
		{
			cancelling = true;
			try
			{
				orig(self);
			}
			finally
			{
				cancelling = false;
			}
		}

		public static void Finish(On.class_92.orig_method_77 orig, class_92 self)
		{
			string path = null;
			try
			{
				var stream = GifStream == null ? null : GifStream.GetValue(self) as FileStream;
				path = stream == null ? null : stream.Name;
			}
			catch (Exception e)
			{
				ZlbbMod.Log("could not find the GIF file: " + e.Message);
			}

			orig(self);
			if (cancelling || path == null)
				return;
			try
			{
				OfferSubmission(path, recordedSolution);
			}
			catch (Exception e)
			{
				ZlbbMod.Log("could not prepare the submission: " + e.Message);
			}
		}

		private static void OfferSubmission(string gifPath, Solution solution)
		{
			if (!ZlbbMod.Enabled || solution == null || ZlbbMod.Sync == null || ZlbbMod.Submitter == null)
				return;

			string author = ZlbbMod.Config.AuthorName;
			string cleaned, reason;
			if (!ZlbbCore.AuthorName.TryClean(author, out cleaned, out reason))
			{
				ZlbbMod.Log("GIF recorded, but no valid author name is set, so nothing is offered for submission (" + reason + ")");
				if (!nameHintShown)
				{
					nameHintShown = true; // once per session, so recording many GIFs is not nagging
					UI.OpenScreen(new NoticeScreen("Submit to zlbb?", "Set your author name first: Mods > ZLBB Leaderboard > Set author name. Then record the GIF again."));
				}
				return;
			}

			Puzzle puzzle = solution.method_1934();
			if (puzzle == null || puzzle.field_2782 || string.IsNullOrEmpty(puzzle.field_2766))
				return; // custom puzzles are not on the leaderboard
			if (ZlbbMod.Sync.Index.CountForPuzzle(puzzle.field_2766) == 0)
			{
				ZlbbMod.Log("GIF recorded for " + puzzle.field_2766 + ", which zlbb does not list; not offering a submission");
				return;
			}

			var info = new FileInfo(gifPath);
			if (!info.Exists || info.Length == 0 || info.Length > MaxGifBytes)
			{
				ZlbbMod.Log("GIF file missing or too large (" + (info.Exists ? info.Length : 0) + " bytes); not offering a submission");
				return;
			}

			byte[] gif = File.ReadAllBytes(gifPath);
			byte[] solutionBytes = solution.method_1959();
			// zlbb's maintainer asks that nothing is submitted without a human decision: the player always has to press SUBMIT
			ZlbbMod.Log("GIF recorded for " + puzzle.field_2766 + " (" + gif.Length + " bytes): asking before submitting");
			UI.OpenScreen(new SubmitScreen(puzzle.field_2767 ?? puzzle.field_2766, cleaned, Path.GetFileName(gifPath), solutionBytes, gif));
		}
	}
}
