using System;
using Quintessential;
using ZlbbCore;

namespace ZlbbLeaderboard
{
	/// <summary>
	/// Big game-style window (same frame and background as Quintessential's Mods screen) that shows every zlbb metric of one puzzle at
	/// once. The window grows to fit the panels; each panel is drawn by the game's own histogram renderer.
	/// </summary>
	internal sealed class AllMetricsScreen : IScreen
	{
		private readonly PuzzleInfo info;
		private int page;
		private bool failed;

		public AllMetricsScreen(PuzzleInfo info)
		{
			this.info = info;
		}

		public bool method_1037()
		{
			return false;
		}

		public void method_47(bool param_4687)
		{
		}

		public void method_48()
		{
		}

		// update & render
		public void method_50(float time)
		{
			if (failed)
			{
				UI.CloseScreen();
				return;
			}
			try
			{
				Draw();
			}
			catch (Exception e)
			{
				failed = true;
				PanelPatch.LogFailureOnce(e);
				UI.CloseScreen();
			}
		}

		private void Draw()
		{
			Vector2 screen = Input.ScreenSize();
			WindowLayout layout = WindowLayouts.Choose(info.Metrics.Count, screen.X, screen.Y);
			Vector2 size = new Vector2(layout.WindowWidth, layout.WindowHeight);
			Vector2 pos = ((screen - size) / 2).Rounded();
			Vector2 bgPos = pos + new Vector2(WindowLayouts.FrameLeft, WindowLayouts.FrameBottom);
			Vector2 bgSize = size - new Vector2(WindowLayouts.FrameWidth, WindowLayouts.FrameHeight);

			UI.DrawLargeUiBackground(bgPos, bgSize);
			UI.DrawUiFrame(pos, size);
			if (UI.DrawAndCheckCloseButton(pos, size, new Vector2(104f, 98f)))
				UI.HandleCloseButton();

			// puzzle title across the top, like the game's own screens
			float headerY = size.Y - 66f - WindowLayouts.Padding - 34f;
			class_140.method_317(info.Name ?? info.Id, pos + new Vector2(WindowLayouts.FrameLeft + WindowLayouts.Padding, headerY),
				(int)(bgSize.X - 2f * WindowLayouts.Padding), true, true);

			int perPage = layout.PanelsPerPage;
			int pages = layout.Pages;
			if (page >= pages)
				page = 0;
			int first = page * perPage;
			for (int i = 0; i < perPage && first + i < info.Metrics.Count; i++)
			{
				float x, y;
				layout.PanelOrigin(i, out x, out y);
				PanelPatch.DrawPanel(PanelPatch.BuildPanel(info, info.Metrics[first + i]), pos + new Vector2(x, y));
			}

			if (pages > 1)
			{
				Vector2 buttonRow = pos + new Vector2(WindowLayouts.FrameLeft + WindowLayouts.Padding, headerY - 6f);
				if (UI.DrawAndCheckSimpleButton("<", buttonRow, new Vector2(44f, 44f)))
					page = (page + pages - 1) % pages;
				if (UI.DrawAndCheckSimpleButton(">", buttonRow + new Vector2(bgSize.X - 2f * WindowLayouts.Padding - 44f, 0f), new Vector2(44f, 44f)))
					page = (page + 1) % pages;
			}

			string note = "zlbb frontier records (record-setting solutions, not every player) - best " + (int)Math.Round(Histogram.DefaultKeepFraction * 100)
				+ "% of scores shown, lower is better" + (pages > 1 ? "  -  page " + (page + 1) + "/" + pages : "");
			UI.DrawText(note, pos + new Vector2(size.X / 2f, WindowLayouts.FrameBottom + WindowLayouts.Padding + 4f), UI.Text, UI.TextColor, TextAlignment.Centred);
		}
	}
}
