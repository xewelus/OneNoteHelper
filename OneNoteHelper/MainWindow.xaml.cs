using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Common;
using CommonWpf;
using CommonWpf.Classes.UI;

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
		}

		private static Regex dateRegex = new Regex("^(\\d+) ([а-€]+) (\\d+) г.$");
		private static Regex timeRegex = new Regex("^(\\d+):(\\d+)$");

		private void ProcessPages_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				string text = Clipboard.GetText();
				if (string.IsNullOrEmpty(text))
				{
					UIHelper.ShowWarning("Clipboard is empty.");
					return;
				}

				if (!Clipboard.ContainsData("OneNote 2010 Internal"))
				{
					UIHelper.ShowWarning("No OneNote data in clipboard.");
					return;
				}

				text = text.Replace("\r\n", "\n");
				string[] lines = text.Split('\n');
				Dictionary<DateTime, StringBuilder> records = GetRecords(lines);

				Dictionary<DateTime, string> records2 = new Dictionary<DateTime, string>();
				foreach (KeyValuePair<DateTime, StringBuilder> pair in records)
				{
					string str = pair.Value.ToString();
					str = str.Trim('\r', '\n');
					records2[pair.Key] = str;
				}

				records2 = records2;
			}
			catch (Exception ex)
			{
				ExceptionHandler.Catch(ex);
			}
		}

		private static Dictionary<DateTime, StringBuilder> GetRecords(string[] lines)
		{
			Dictionary<DateTime, StringBuilder> records = new Dictionary<DateTime, StringBuilder>();
			int lineNum = 0;
			StringBuilder sb = null;
			LinesContext context = LinesContext.NeedDate;
			DateTime? date = null;
			int emptyCount = 0;
			foreach (string line in lines)
			{
				if (string.IsNullOrEmpty(line))
				{
					emptyCount++;
				}
				else
				{
					emptyCount = 0;
				}

				lineNum++;
				try
				{
					if (context == LinesContext.NeedDate)
					{
						if (string.IsNullOrEmpty(line)) continue;

						Match match = dateRegex.Match(line);
						if (!match.Success)
						{
							throw new Exception("Date not found.");
						}

						date = GetDay(match);

						context = LinesContext.NeedTime;
					}
					else if (context == LinesContext.NeedTime)
					{
						if (date == null) throw new NullReferenceException(nameof(date));

						Match match = timeRegex.Match(line);
						if (!match.Success)
						{
							throw new Exception("Time not found.");
						}

						TimeSpan time = GetTime(match);
						date = date.Value.Add(time);

						sb = records.GetOrCreate(date.Value);
						context = LinesContext.NeedText;
					}
					else if (context == LinesContext.NeedText)
					{
						if (sb == null) throw new NullReferenceException(nameof(sb));

						if (sb.Length > 0) sb.AppendLine();

						sb.Append(line);
						if (emptyCount > 3)
						{
							context = LinesContext.NeedDate;
							sb = null;
						}
					}
				}
				catch (Exception ex)
				{
					throw new Exception($"Error while processing line #{lineNum}: {line}", ex);
				}
			}
			return records;
		}

		private static DateTime GetDay(Match match)
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

			return new DateTime(year, month, day);
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


		private enum LinesContext
		{
			NeedDate,
			NeedTime,
			NeedText
		}
	}
}
