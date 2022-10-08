using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Common;
using Common.Classes.Diagnostic;
using CommonWpf;
using CommonWpf.Classes.UI;
using TestOneNote.Tests;

namespace OneNoteHelper
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public MainWindow()
		{
			this.InitializeComponent();

			this.SaveResultButton.Visibility = Visibility.Hidden;

			this.ListView.Visibility = Visibility.Hidden;
			this.ListView.ItemsSource = null;
		}

		private static readonly Regex dateRegex = new Regex("<p style='[\\s\\S-[\\r\\n]]*?>(\\d+)\\s+(\\w+)\\s+(\\d+)\\s+г.</p>");
		private static readonly Regex timeRegex = new Regex("<p style='[\\s\\S-[\\r\\n]]*?>(\\d+):(\\d+)</p>");

		public List<Record> Records { get; set; }

		private bool isTest = true;
		private Match lastMatch;
		private void ProcessPages_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				string text;

				if (this.isTest)
				{
					text = TestResources.Test_html;
				}
				else
				{
					text = Clipboard.GetData("HTML Format") as string;

					if (string.IsNullOrEmpty(text))
					{
						UIHelper.ShowWarning("No OneNote data in clipboard.");
						return;
					}
				}

				Regex outerRegex = new Regex("([\\s\\S]*)(<html[\\s\\S]*<!--StartFragment-->)([\\s\\S]*)(<!--EndFragment-->[\\s\\S]*)");
				Match match = outerRegex.Match(text);
				if (!match.Success)
				{
					UIHelper.ShowWarning("Invalid OneNote data in clipboard.");
					Diag.SaveAndOpenLog(text);
					this.lastMatch = null;
					return;
				}

				this.lastMatch = match;

				string str1 = match.Groups[3].Value;

				string[] blocks = Regex.Split(str1, "<p style='margin:0in'>&nbsp;</p>\r\n\r\n");

				List<Record> records = GetRecords(blocks);

				records.Sort(CompareRecords);

				this.Records = records;
				this.ListView.ItemsSource = this.Records;
				this.ListView.Visibility = Visibility.Visible;

				this.SaveResultButton.Visibility = Visibility.Visible;
				this.SaveResultButton.IsEnabled = records.Any(r => !r.HasError);
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}

		private static List<Record> GetRecords(string[] blocks)
		{
			List<Record> records = new List<Record>();
			foreach (string block in blocks)
			{
				try
				{
					ProcessBlock(block, records);
				}
				catch (Exception ex)
				{
					throw new Exception($"Error while processing block '{block}'.", ex);
				}
			}
			return records;
		}

		private static void ProcessBlock(string block, List<Record> records)
		{
			string[] lines = block.Replace("\r\n", "\n").Split('\n');

			StringBuilder sb = new StringBuilder();
			LinesContext context = LinesContext.NeedDate;
			DateTime? date = null;

			Record record = new Record();
			records.Add(record);
			record.Index = records.Count;
			record.Block = block;

			for (int lineNum = 0; lineNum < lines.Length; lineNum++)
			{
				string line = lines[lineNum];

				if (line.StartsWith("<p "))
				{
					// append all lines until empty because of line wrapping
					while (lineNum < lines.Length - 1)
					{
						string nextLine = lines[lineNum + 1];
						if (nextLine.Length == 0) break;

						line = $"{line} {nextLine}";
						lineNum++;
					}
				}

				try
				{
					if (context == LinesContext.NeedTime)
					{
						if (line.Length == 0) continue;

						if (date == null) throw new NullReferenceException(nameof(date));

						Match timeMatch = timeRegex.Match(line);
						if (!timeMatch.Success)
						{
							throw new Exception("Time not found.");
						}

						TimeSpan time = GetTime(timeMatch);
						date = date.Value.Add(time);

						record.Date = date.Value;

						// remove empty lines after previous parsing
						while (sb.Length > 0 && sb[sb.Length - 1].In('\r', '\n'))
						{
							sb.Remove(sb.Length - 1, 1);
						}

						context = LinesContext.NeedText;
						continue;
					}

					if (context == LinesContext.NeedText)
					{
						if (sb == null) throw new NullReferenceException(nameof(sb));
						if (sb.Length > 0) sb.AppendLine();
						sb.Append(line);
						continue;
					}

					if (string.IsNullOrEmpty(line)) continue;

					if (Regex.IsMatch(line, "<p style='margin:0in;[\\s\\S-[\\r\\n]]*?>&nbsp;</p>"))
					{
						continue;
					}

					Match match = dateRegex.Match(line);
					if (!match.Success)
					{
						throw new Exception("Date not found.");
					}

					(date, record.DateString) = GetDay(match);

					context = LinesContext.NeedTime;
				}
				catch (Exception ex)
				{
					record.Error = new Exception($"Error while processing line #{lineNum}: {line}", ex);
					break;
				}
			}

			if (sb.Length > 0)
			{
				record.Text = sb.ToString();
			}
		}

		private static (DateTime Date, string DateString) GetDay(Match match)
		{
			string s1 = match.Groups[1].Value;
			int day;

			try
			{
				day = int.Parse(s1);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing day '{s1}'.", ex);
			}

			string s2 = match.Groups[2].Value;

			int month;
			try
			{
				month = GetMonth(s2);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing month '{s2}'.", ex);
			}

			string s3 = match.Groups[3].Value;
			int year;
			try
			{
				year = int.Parse(s3);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing year '{s3}'.", ex);
			}

			return (new DateTime(year, month, day), $"{s1} {s2} {s3} г.");
		}

		private static readonly string[] months = 
		{
			"€нвар€",
			"феврал€",
			"марта",
			"апрел€",
			"ма€",
			"июн€",
			"июл€",
			"августа",
			"сент€бр€",
			"окт€бр€",
			"но€бр€",
			"декабр€"
		};

		private static int GetMonth(string str)
		{
			int index = Array.IndexOf(months, str);
			if (index == -1)
			{
				throw new Exception($"Invalid month: {str}");
			}
			return index + 1;
		}

		private static TimeSpan GetTime(Match match)
		{
			string s1 = match.Groups[1].Value;
			int hour;

			try
			{
				hour = int.Parse(s1);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing hour '{s1}'.", ex);
			}

			string s2 = match.Groups[2].Value;

			int minutes;
			try
			{
				minutes = int.Parse(s2);
			}
			catch (Exception ex)
			{
				throw new Exception($"Error parsing minutes '{s2}'.", ex);
			}

			return new TimeSpan(
				hours: hour, 
				minutes: minutes, 
				seconds: 0);
		}

		private static int CompareRecords(Record r1, Record r2)
		{
			int res = Comparer<bool>.Default.Compare(r1.Error == null, r2.Error == null);
			if (res != 0) return res;

			if (r1.Error == null)
			{
				res = Comparer<DateTime>.Default.Compare(r1.Date, r2.Date);
				if (res != 0) return res;
			}

			return Comparer<int>.Default.Compare(r1.Index, r2.Index);
		}

		private void SaveResultButton_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				int errorsCount = this.Records.Select(r => r.HasError).Count();
				if (errorsCount > 0)
				{
					if (!UIHelper.AskYesNo($"Results contains some errors ({errorsCount}). Continue?"))
					{
						return;
					}
				}

				StringBuilder sb = new StringBuilder();

				// beginning part
				sb.AppendLine(this.lastMatch.Groups[2].Value);

				sb.AppendLine("<table border=1 cellpadding=0 cellspacing=0 valign=top>");
				foreach (Record record in this.Records)
				{
					if (record.HasError) continue;

					sb.AppendLine("<tr>");
					sb.AppendLine("<td>");
					sb.AppendLine($"<p style='margin:0in;font-family:Consolas;font-size:10.0pt'>{record.DateString}</p>");
					sb.AppendLine($"<p style='margin:0in;font-family:Consolas;font-size:10.0pt'>{record.Date:HH:mm}</p>");
					sb.AppendLine("<p style='margin:0in;font-family:Consolas;font-size:10.0pt' lang=x-none>&nbsp;</p>");
					sb.AppendLine(record.Text);
					sb.AppendLine("</td>");
					sb.AppendLine("<td>");
					sb.AppendLine("</td>");
					sb.AppendLine("</tr>");
				}
				sb.AppendLine("</table>");

				// ending part
				sb.AppendLine(this.lastMatch.Groups[4].Value);

				Diag.SaveAndOpenLog(sb.ToString(), "result_", ".html");
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}

		private enum LinesContext
		{
			NeedDate,
			NeedTime,
			NeedText
		}
	}
}
