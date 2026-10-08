using System;
using System.IO;
using System.Linq;
using System.Threading;
using Quintessential;
using SDL2;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>Shared look of the small centred windows (same layout as Quintessential's own NoticeScreen).</summary>
	internal static class DialogDraw
	{
		public static Vector2 Center()
		{
			return Input.ScreenSize() / 2;
		}

		public static void Title(string text)
		{
			UI.DrawText(text, Center() + new Vector2(0f, 150f), UI.Title, Color.White, TextAlignment.Centred);
		}

		/// <summary>Lines of text below the title, top to bottom.</summary>
		public static void Lines(params string[] lines)
		{
			Vector2 c = Center();
			for (int i = 0; i < lines.Length; i++)
				if (!string.IsNullOrEmpty(lines[i]))
					UI.DrawText(lines[i], c + new Vector2(0f, 90f - 34f * i), UI.SubTitle, class_181.field_1718, TextAlignment.Centred);
		}

		public static bool Button(string label, float x)
		{
			return UI.DrawAndCheckBoxButton(label, Center() + new Vector2(x, -160f));
		}

		public static bool EscapePressed()
		{
			return Input.IsSdlKeyPressed(SDL.enum_160.SDLK_ESCAPE);
		}
	}

	/// <summary>Small text-entry window for the author name (Quintessential's settings screen has no text field).</summary>
	internal sealed class AuthorNameScreen : IScreen
	{
		private static readonly string[] Letters = "abcdefghijklmnopqrstuvwxyz".Select(c => c.ToString()).ToArray();
		private static readonly string[] Digits = "0123456789".Select(c => c.ToString()).ToArray();

		private string text;
		private string error;
		private float time;

		public AuthorNameScreen()
		{
			text = ZlbbMod.Config.AuthorName ?? "";
		}

		public bool method_1037() { return false; }
		public void method_47(bool param_4687) { GameLogic.field_2434.field_2464 = true; }
		public void method_48() { }

		public void method_50(float delta)
		{
			time += delta;
			Type();

			DialogDraw.Title("Author name");
			DialogDraw.Lines(
				"The name your solutions are published under on zlbb.faendir.com.",
				"Use your own name - for example your Reddit or Discord username.",
				"It should identify you as a person, not this mod. (Letters, digits, space, _ - . up to " + AuthorName.MaxLength + ")");
			bool caret = ((int)(time * 2f)) % 2 == 0;
			UI.DrawText(text + (caret ? "_" : " "), DialogDraw.Center() + new Vector2(0f, -30f), UI.Title, Color.White, TextAlignment.Centred);
			if (error != null)
				UI.DrawText(error, DialogDraw.Center() + new Vector2(0f, -85f), UI.SubTitle, Color.FromHex(16752192), TextAlignment.Centred);

			bool ok = DialogDraw.Button("OK", -290f) || Input.IsSdlKeyPressed(SDL.enum_160.SDLK_RETURN);
			bool cancel = DialogDraw.Button("CANCEL", 30f) || DialogDraw.EscapePressed();
			if (cancel)
			{
				UI.CloseScreen();
			}
			else if (ok)
			{
				string cleaned, reason;
				if (AuthorName.TryClean(text, out cleaned, out reason))
				{
					ZlbbMod.Config.AuthorName = cleaned;
					ZlbbMod.SaveConfig();
					ZlbbMod.Log("author name set");
					UI.CloseScreen();
				}
				else
					error = reason;
			}
		}

		private void Type()
		{
			if (Input.IsSdlKeyPressed(SDL.enum_160.SDLK_BACKSPACE) && text.Length > 0)
				text = text.Substring(0, text.Length - 1);
			if (text.Length >= AuthorName.MaxLength)
				return;
			bool shift = Input.IsShiftHeld();
			foreach (string letter in Letters)
				if (Input.IsKeyPressed(letter))
					text += shift ? letter.ToUpperInvariant() : letter;
			foreach (string digit in Digits)
				if (Input.IsKeyPressed(digit))
					text += digit;
			if (Input.IsKeyPressed("Space"))
				text += " ";
			if (Input.IsKeyPressed("-"))
				text += shift ? "_" : "-";
			if (Input.IsKeyPressed("."))
				text += ".";
			if (text.Length > AuthorName.MaxLength)
				text = text.Substring(0, AuthorName.MaxLength);
			error = null;
		}
	}

	/// <summary>Asks a yes/no question before something that costs a download.</summary>
	internal sealed class ConfirmScreen : IScreen
	{
		private readonly string title;
		private readonly string[] lines;
		private readonly string yesLabel;
		private readonly Action onYes;

		public ConfirmScreen(string title, string[] lines, string yesLabel, Action onYes)
		{
			this.title = title;
			this.lines = lines;
			this.yesLabel = yesLabel;
			this.onYes = onYes;
		}

		public bool method_1037() { return false; }
		public void method_47(bool param_4687) { GameLogic.field_2434.field_2464 = true; }
		public void method_48() { }

		public void method_50(float delta)
		{
			DialogDraw.Title(title);
			DialogDraw.Lines(lines);
			if (DialogDraw.Button(yesLabel, -290f))
			{
				UI.CloseScreen();
				onYes();
			}
			else if (DialogDraw.Button("CANCEL", 30f) || DialogDraw.EscapePressed())
				UI.CloseScreen();
		}
	}

	/// <summary>What is stored locally and how old it is (no request is made).</summary>
	internal sealed class DataStatusScreen : IScreen
	{
		public bool method_1037() { return false; }
		public void method_47(bool param_4687) { GameLogic.field_2434.field_2464 = true; }
		public void method_48() { }

		public void method_50(float delta)
		{
			SyncService sync = ZlbbMod.Sync;
			DialogDraw.Title("zlbb leaderboard data");
			if (sync == null)
				DialogDraw.Lines("The mod did not start, so there is no data.");
			else
			{
				DateTime? last = sync.LastSyncUtc;
				string age = last.HasValue ? "last synced " + last.Value.ToLocalTime().ToString("g") : "never synced";
				string state;
				switch (sync.State)
				{
					case SyncState.Running: state = "syncing right now"; break;
					case SyncState.Fresh: state = "up to date as of this start"; break;
					case SyncState.Stale: state = "could not reach zlbb at the last attempt; using the saved copy"; break;
					case SyncState.Failed: state = "could not reach zlbb and nothing is saved yet"; break;
					default: state = "not started"; break;
				}
				DialogDraw.Lines(sync.Index.Count + " records for " + sync.Index.PuzzleCount + " puzzles", age, state,
					"It refreshes once when the game starts (and with the Sync button).");
			}
			if (DialogDraw.Button("OK", -130f) || DialogDraw.EscapePressed())
				UI.CloseScreen();
		}
	}

	/// <summary>Result of the "Sync with zlbb now" button.</summary>
	internal sealed class SyncStatusScreen : IScreen
	{
		private readonly Func<SyncOutcome> outcome;
		private float time;

		public SyncStatusScreen(Func<SyncOutcome> outcome)
		{
			this.outcome = outcome;
		}

		public bool method_1037() { return false; }
		public void method_47(bool param_4687) { GameLogic.field_2434.field_2464 = true; }
		public void method_48() { }

		public void method_50(float delta)
		{
			time += delta;
			SyncOutcome result = outcome();
			SyncService sync = ZlbbMod.Sync;
			DialogDraw.Title("zlbb leaderboard data");
			if (result == null)
			{
				DialogDraw.Lines("Syncing" + new string('.', 1 + ((int)(time * 2f)) % 3));
				return;
			}
			if (result.Skipped != null)
				DialogDraw.Lines("Not synced: " + result.Skipped + ".");
			else if (result.State == SyncState.Fresh && sync != null)
				DialogDraw.Lines("Up to date: " + sync.Index.Count + " records for " + sync.Index.PuzzleCount + " puzzles.",
					result.Bootstrap ? "Everything was downloaded again." : result.Changes + (result.Changes == 1 ? " change" : " changes") + " since the last sync.");
			else
				DialogDraw.Lines("Could not reach zlbb" + (result.Error == null ? "." : ": " + result.Error),
					sync != null && sync.Index.Count > 0 ? "Using the saved copy (" + sync.Index.Count + " records)." : "No leaderboard data available yet.");

			if (DialogDraw.Button("OK", -130f) || DialogDraw.EscapePressed())
				UI.CloseScreen();
		}
	}

	/// <summary>
	/// Asks before anything is sent, uploads in the background and shows the result. Nothing leaves the PC
	/// before the player presses SUBMIT here.
	/// </summary>
	internal sealed class SubmitScreen : IScreen
	{
		private enum Step { Ask, Uploading, Done }

		private readonly string puzzleName;
		private readonly string author;
		private readonly string gifName;
		private readonly byte[] solution;
		private readonly byte[] gif;
		private Step step = Step.Ask;
		private volatile SubmitResult result;
		private float time;

		public SubmitScreen(string puzzleName, string author, string gifName, byte[] solution, byte[] gif)
		{
			this.puzzleName = puzzleName;
			this.author = author;
			this.gifName = gifName;
			this.solution = solution;
			this.gif = gif;
		}

		public bool method_1037() { return false; }
		public void method_47(bool param_4687) { GameLogic.field_2434.field_2464 = true; }
		public void method_48() { }

		public void method_50(float delta)
		{
			time += delta;
			switch (step)
			{
				case Step.Ask:
					DialogDraw.Title("Submit to zlbb?");
					DialogDraw.Lines(
						puzzleName,
						"as \"" + author + "\"",
						"Your solution, the GIF and its file name are sent to " + ZlbbMod.SubmitHost + " and become public there.",
						"(" + (gif.Length / 1024) + " KB GIF)");
					if (DialogDraw.Button("SUBMIT", -290f))
						StartUpload();
					else if (DialogDraw.Button("NOT NOW", 30f) || DialogDraw.EscapePressed())
						UI.CloseScreen();
					break;

				case Step.Uploading:
					DialogDraw.Title("Submitting to zlbb");
					DialogDraw.Lines("Uploading" + new string('.', 1 + ((int)(time * 2f)) % 3), "zlbb checks the solution before it answers; this can take a while.");
					if (result != null)
						step = Step.Done;
					else if (DialogDraw.Button("HIDE", -130f) || DialogDraw.EscapePressed())
					{
						ZlbbMod.Log("submission window hidden; the upload carries on in the background");
						UI.CloseScreen(); // never trap the player in a slow upload; the result is logged when it arrives
					}
					break;

				default:
					DialogDraw.Title(result.Outcome == SubmitOutcome.Failure ? "Not submitted" : "Submitted");
					DialogDraw.Lines(result.Describe(), Shorten(result.Message));
					if (DialogDraw.Button("OK", -130f) || DialogDraw.EscapePressed())
						UI.CloseScreen();
					break;
			}
		}

		private void StartUpload()
		{
			step = Step.Uploading;
			var thread = new Thread(() =>
			{
				try
				{
					result = ZlbbMod.Submitter.Submit(author, solution, gif, gifName);
					ZlbbMod.Log("submission finished: " + result.Outcome);
				}
				catch (Exception e)
				{
					result = new SubmitResult { Outcome = SubmitOutcome.Failure, Message = e.Message };
					ZlbbMod.Log("submission crashed: " + e);
				}
			});
			thread.IsBackground = true;
			thread.Name = "ZlbbLeaderboard submit";
			thread.Start();
		}

		private static string Shorten(string text)
		{
			if (string.IsNullOrEmpty(text))
				return null;
			return text.Length > 90 ? text.Substring(0, 90) + "..." : text;
		}
	}
}
