using System;

namespace ZlbbCore
{
	/// <summary>Grid of histogram panels inside one big game-style window.</summary>
	public sealed class WindowLayout
	{
		public int Columns;
		public int Rows;
		public int PanelCount;
		public float WindowWidth;
		public float WindowHeight;

		public int PanelsPerPage
		{
			get { return Columns * Rows; }
		}

		public int Pages
		{
			get { return Math.Max(1, (PanelCount + PanelsPerPage - 1) / PanelsPerPage); }
		}

		/// <summary>False when even the best grid is bigger than the screen (tiny resolutions); the window is then clamped by the caller.</summary>
		public bool Fits;

		/// <summary>Bottom-left corner of a panel relative to the window's bottom-left corner (the game's y axis points up).</summary>
		public void PanelOrigin(int indexOnPage, out float x, out float y)
		{
			int column = indexOnPage % Columns;
			int row = indexOnPage / Columns; // 0 = top row
			x = WindowLayouts.FrameLeft + WindowLayouts.Padding + column * (WindowLayouts.PanelWidth + WindowLayouts.Gap);
			y = WindowLayouts.FrameBottom + WindowLayouts.Padding + WindowLayouts.FooterHeight + (Rows - 1 - row) * (WindowLayouts.PanelHeight + WindowLayouts.Gap);
		}
	}

	/// <summary>
	/// Sizes the big "all metrics" window. The game's histogram panel is a fixed 258 x 376 px sprite, so instead of scaling it the window
	/// grows to fit as many panels as the screen allows (all 12 on any normal widescreen), paging only on very small screens.
	/// </summary>
	public static class WindowLayouts
	{
		public const float PanelWidth = 258f;
		public const float PanelHeight = 376f;
		public const float Gap = 14f;
		public const float Padding = 20f;
		/// <summary>Room above the panels for the puzzle title.</summary>
		public const float HeaderHeight = 60f;
		/// <summary>Room below the panels for the footnote.</summary>
		public const float FooterHeight = 28f;
		// the game's window frame art: inner area starts 78 px from the left and 88 px from the bottom; the frame takes 156 x 154 px in total
		public const float FrameLeft = 78f;
		public const float FrameBottom = 88f;
		public const float FrameWidth = 156f;
		public const float FrameHeight = 154f;
		public const int MaxColumns = 9;
		/// <summary>Space kept free around the window.</summary>
		public const float ScreenMargin = 20f;

		public static WindowLayout Choose(int panelCount, float screenWidth, float screenHeight)
		{
			if (panelCount < 1)
				panelCount = 1;
			WindowLayout best = null;
			for (int columns = Math.Min(panelCount, MaxColumns); columns >= 1; columns--)
			{
				float width = Width(columns);
				if (width > screenWidth - ScreenMargin)
					continue;
				int fitRows = (int)Math.Floor((screenHeight - ScreenMargin - FrameHeight - 2 * Padding - HeaderHeight - FooterHeight + Gap) / (PanelHeight + Gap));
				if (fitRows < 1)
					continue;
				int rows = Math.Min(fitRows, (panelCount + columns - 1) / columns);
				var candidate = new WindowLayout { Columns = columns, Rows = rows, PanelCount = panelCount, Fits = true };
				candidate.WindowWidth = width;
				candidate.WindowHeight = Height(rows);
				// show as many panels per page as possible (empty slots do not count); then fewer rows; then fewer empty slots
				int shown = Math.Min(candidate.PanelsPerPage, panelCount);
				int bestShown = best == null ? -1 : Math.Min(best.PanelsPerPage, panelCount);
				bool better = best == null
					|| shown > bestShown
					|| (shown == bestShown && candidate.Rows < best.Rows)
					|| (shown == bestShown && candidate.Rows == best.Rows && candidate.PanelsPerPage < best.PanelsPerPage);
				if (better)
					best = candidate;
			}
			if (best != null)
				return best;
			return new WindowLayout { Columns = 1, Rows = 1, PanelCount = panelCount, WindowWidth = Width(1), WindowHeight = Height(1), Fits = false };
		}

		public static float Width(int columns)
		{
			return columns * PanelWidth + (columns - 1) * Gap + 2 * Padding + FrameWidth;
		}

		public static float Height(int rows)
		{
			return rows * PanelHeight + (rows - 1) * Gap + 2 * Padding + HeaderHeight + FooterHeight + FrameHeight;
		}
	}
}
