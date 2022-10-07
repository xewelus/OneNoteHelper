using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using Common;
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

			this.ListView.Visibility = Visibility.Hidden;
			this.ListView.ItemsSource = null;
		}

		private static readonly Regex dateRegex = new Regex("^(\\d+) ([а-€]+) (\\d+) г.$");
		private static readonly Regex timeRegex = new Regex("^(\\d+):(\\d+)$");

		public List<Record> Records { get; set; }

		private bool isTest = false;
		private void ProcessPages_OnClick(object sender, RoutedEventArgs e)
		{
			try
			{
				string text;

				if (this.isTest)
				{
					text = TestResources.TestRecords;
				}
				else
				{
					text = Clipboard.GetText();
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
				}

				this.ListView.Visibility = Visibility.Visible;

				text = text.Replace("\r\n", "\n");
				string[] lines = text.Split('\n');
				Dictionary<DateTime, StringBuilder> records = GetRecords(lines);

				// group records by date, then by time to get united text for each day
				this.Records =
					records
						.Group(r => r.Key.Date)
						.OrderBy(g => g.Key)
						.Select
						(
							g => new Record
							{
								Date = g.Key,
								Text = string.Join
								(
									separator: "\r\n\r\n",
									values: g.Group(p => p.Key.TimeOfDay)
									         .OrderBy(g2 => g2.Key)
									         .Select
									         (
										         g2 => string.Join
										         (
											         "\r\n\r\n",
											         g2.Select(p => $"{g2.Key.Hours:00}:{g2.Key.Minutes:00}\r\n\r\n{p.Value.ToString().Trim('\r', '\n')}")
										         )
									         )
								)
							}
						)
						.ToList();

				this.ListView.ItemsSource = this.Records;
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
				lineNum++;

				if (line.Length == 0)
				{
					emptyCount++;
				}
				else
				{
					emptyCount = 0;
				}

				try
				{
					if (context == LinesContext.NeedTime)
					{
						if (date == null) throw new NullReferenceException(nameof(date));

						Match timeMatch = timeRegex.Match(line);
						if (!timeMatch.Success)
						{
							throw new Exception("Time not found.");
						}

						TimeSpan time = GetTime(timeMatch);
						date = date.Value.Add(time);

						sb = records.GetOrCreate(date.Value);

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

						Match dateMatch = dateRegex.Match(line);
						if (dateMatch.Success)
						{
							context = LinesContext.NeedDate;
							sb = null;
						}
						else
						{
							if (emptyCount > 4)
							{
								throw new Exception($"To much empty lines after '{sb}'.");
							}

							if (sb.Length > 0) sb.AppendLine();

							sb.Append(line);
							continue;
						}
					}

					if (string.IsNullOrEmpty(line)) continue;

					Match match = dateRegex.Match(line);
					if (!match.Success)
					{
						throw new Exception("Date not found.");
					}

					date = GetDay(match);

					context = LinesContext.NeedTime;
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
