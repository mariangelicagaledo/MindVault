using SQLite;

namespace mindvault.Data;

public class Reviewer
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class Flashcard
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int ReviewerId { get; set; }

    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;

    // Optional images for front/back
    public string QuestionImagePath { get; set; } = string.Empty;
    public string AnswerImagePath { get; set; } = string.Empty;

    public bool Learned { get; set; }
    public int Order { get; set; }
}
