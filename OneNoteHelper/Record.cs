using System;

namespace OneNoteHelper;

public class Record
{
	public int Index { get; set; }
	public DateTime Date { get; set; }
	public string DateString { get; set; }
	public string Text { get; set; }
	public string Block { get; set; }
	public Exception Error { get; set; }

	public bool HasError
	{
		get
		{
			return this.Error != null;
		}
	}

	public string DisplayText
	{
		get
		{
			if (this.Error == null)
			{
				return this.Text;
			}

			return $"{this.Error}\r\n\r\n{this.Block}";
		}
	}

	public override string ToString()
	{
		return $"Record #{this.Index} {this.Date:dd.MM.yyyy HH:mm} {this.Error} {this.Text}";
	}
}