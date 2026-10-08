using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace ZlbbCore.Tests
{
	public class MetricOrderTests
	{
		private static string Keys(IEnumerable<Metric> metrics)
		{
			return string.Join(",", metrics.Select(m => m.Key));
		}

		[Fact]
		public void Vanilla_trio_depends_on_the_puzzle_type()
		{
			Assert.Equal("g,c,a", Keys(MetricPages.Vanilla(false)));
			Assert.Equal("g,c,i", Keys(MetricPages.Vanilla(true)));
		}

		[Fact]
		public void The_sum_is_always_first_then_the_games_own_panels_then_derived_then_the_rest()
		{
			Assert.Equal("sum,g,c,a,sum4,gc,ga,ca,i,h,w,b,r,aI,hI,wI,bI", Keys(MetricPages.DisplayOrder(false, m => true)));
			Assert.Equal("sumP,g,c,i,gc,gi,ci,a,h,w,b,r,aI,hI,wI,bI", Keys(MetricPages.DisplayOrder(true, m => true)));
		}

		[Fact]
		public void The_sum_stays_first_whatever_else_is_missing()
		{
			var onlySumAndCost = MetricPages.DisplayOrder(false, m => m == Metrics.Sum || m == Metrics.Cost || m == Metrics.Rate);
			Assert.Equal("sum,g,r", Keys(onlySumAndCost));
		}

		[Fact]
		public void Metrics_without_data_are_left_out()
		{
			var noGeometry = new HashSet<string> { "h", "w", "b", "hI", "wI", "bI" };
			Assert.Equal("sumP,g,c,i,gc,gi,ci,a,r,aI", Keys(MetricPages.DisplayOrder(true, m => !noGeometry.Contains(m.Key))));
			Assert.Empty(MetricPages.DisplayOrder(false, m => false));
		}

		[Fact]
		public void Every_metric_appears_exactly_once()
		{
			var normal = MetricPages.DisplayOrder(false, m => true);
			Assert.Equal(17, normal.Count);
			Assert.Equal(17, normal.Distinct().Count());
			var production = MetricPages.DisplayOrder(true, m => true);
			Assert.Equal(16, production.Count);
			Assert.Equal(16, production.Distinct().Count());
		}
	}

	public class WindowLayoutTests
	{
		[Theory]
		[InlineData(3440, 1440)]
		[InlineData(2560, 1440)]
		[InlineData(1920, 1080)]
		public void All_twelve_panels_fit_at_common_resolutions(int width, int height)
		{
			WindowLayout layout = WindowLayouts.Choose(12, width, height);
			Assert.True(layout.Fits);
			Assert.Equal(12, layout.PanelsPerPage);
			Assert.Equal(1, layout.Pages);
			Assert.True(layout.WindowWidth <= width - WindowLayouts.ScreenMargin);
			Assert.True(layout.WindowHeight <= height - WindowLayouts.ScreenMargin);
		}

		[Fact]
		public void Widescreen_uses_six_columns_by_two_rows()
		{
			WindowLayout layout = WindowLayouts.Choose(12, 3440, 1440);
			Assert.Equal(6, layout.Columns);
			Assert.Equal(2, layout.Rows);
		}

		[Fact]
		public void All_seventeen_panels_fit_on_one_page_of_a_very_wide_screen()
		{
			WindowLayout layout = WindowLayouts.Choose(17, 3440, 1440);
			Assert.True(layout.Fits);
			Assert.Equal(1, layout.Pages);
			Assert.Equal(9, layout.Columns);
			Assert.Equal(2, layout.Rows);
			Assert.True(layout.WindowWidth <= 3440 - WindowLayouts.ScreenMargin);
		}

		[Fact]
		public void On_a_full_hd_screen_seventeen_panels_take_two_pages()
		{
			WindowLayout layout = WindowLayouts.Choose(17, 1920, 1080);
			Assert.True(layout.Fits);
			Assert.Equal(2, layout.Pages);
			Assert.True(layout.WindowWidth <= 1920 - WindowLayouts.ScreenMargin);
		}

		[Fact]
		public void Fewer_panels_make_a_smaller_window()
		{
			WindowLayout twelve = WindowLayouts.Choose(12, 3440, 1440);
			WindowLayout six = WindowLayouts.Choose(6, 3440, 1440);
			WindowLayout three = WindowLayouts.Choose(3, 3440, 1440);
			Assert.Equal(1, six.Rows);
			Assert.Equal(6, six.Columns);
			Assert.Equal(3, three.Columns);
			Assert.True(six.WindowHeight < twelve.WindowHeight);
			Assert.True(three.WindowWidth < six.WindowWidth);
		}

		[Fact]
		public void Smaller_screens_page_instead_of_overflowing()
		{
			WindowLayout layout = WindowLayouts.Choose(12, 1366, 768);
			Assert.True(layout.Fits);
			Assert.True(layout.PanelsPerPage < 12);
			Assert.True(layout.Pages > 1);
			Assert.True(layout.WindowWidth <= 1366 - WindowLayouts.ScreenMargin);
			Assert.True(layout.WindowHeight <= 768 - WindowLayouts.ScreenMargin);
			Assert.True(layout.Pages * layout.PanelsPerPage >= 12);
			Assert.True((layout.Pages - 1) * layout.PanelsPerPage < 12); // no completely empty last page
		}

		[Fact]
		public void A_tiny_screen_reports_that_nothing_fits_but_still_returns_a_layout()
		{
			WindowLayout layout = WindowLayouts.Choose(12, 640, 480);
			Assert.False(layout.Fits);
			Assert.Equal(1, layout.PanelsPerPage);
			Assert.Equal(12, layout.Pages);
		}

		[Fact]
		public void Panels_do_not_overlap_and_stay_inside_the_window()
		{
			foreach (int count in new[] { 1, 3, 6, 9, 12 })
			{
				WindowLayout layout = WindowLayouts.Choose(count, 3440, 1440);
				var rects = new List<float[]>();
				for (int i = 0; i < System.Math.Min(count, layout.PanelsPerPage); i++)
				{
					float x, y;
					layout.PanelOrigin(i, out x, out y);
					Assert.True(x >= WindowLayouts.FrameLeft && x + WindowLayouts.PanelWidth <= layout.WindowWidth - (WindowLayouts.FrameWidth - WindowLayouts.FrameLeft));
					Assert.True(y >= WindowLayouts.FrameBottom && y + WindowLayouts.PanelHeight <= layout.WindowHeight - (WindowLayouts.FrameHeight - WindowLayouts.FrameBottom) - WindowLayouts.HeaderHeight + 0.001f);
					rects.Add(new[] { x, y, x + WindowLayouts.PanelWidth, y + WindowLayouts.PanelHeight });
				}
				for (int a = 0; a < rects.Count; a++)
					for (int b = a + 1; b < rects.Count; b++)
						Assert.True(rects[a][2] <= rects[b][0] || rects[b][2] <= rects[a][0] || rects[a][3] <= rects[b][1] || rects[b][3] <= rects[a][1],
							"panels " + a + " and " + b + " overlap for count " + count);
			}
		}

		[Fact]
		public void Panel_order_is_left_to_right_then_top_to_bottom()
		{
			WindowLayout layout = WindowLayouts.Choose(12, 3440, 1440);
			float x0, y0, x1, y1, x6, y6;
			layout.PanelOrigin(0, out x0, out y0);
			layout.PanelOrigin(1, out x1, out y1);
			layout.PanelOrigin(6, out x6, out y6);
			Assert.True(x1 > x0);
			Assert.Equal(y0, y1);
			Assert.Equal(x0, x6);
			Assert.True(y6 < y0); // the second row is lower on screen (y axis points up)
		}
	}
}
