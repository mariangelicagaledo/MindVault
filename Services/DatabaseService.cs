using SQLite;
using mindvault.Data;

namespace mindvault.Services;

public class DatabaseService
{
    readonly SQLiteAsyncConnection _db;

    public DatabaseService(string dbPath)
    {
        _db = new SQLiteAsyncConnection(dbPath);
    }

    public async Task InitializeAsync()
    {
        await _db.CreateTableAsync<Reviewer>();
        await _db.CreateTableAsync<Flashcard>();
        // Try add new columns when upgrading from older schema
        try { await _db.ExecuteAsync("ALTER TABLE Flashcard ADD COLUMN QuestionImagePath TEXT"); } catch { }
        try { await _db.ExecuteAsync("ALTER TABLE Flashcard ADD COLUMN AnswerImagePath TEXT"); } catch { }
    }

    public Task<int> AddReviewerAsync(Reviewer reviewer) => _db.InsertAsync(reviewer);
    public Task<int> AddFlashcardAsync(Flashcard card) => _db.InsertAsync(card);

    public Task<List<Reviewer>> GetReviewersAsync() => _db.Table<Reviewer>().OrderByDescending(r => r.Id).ToListAsync();
    public Task<List<Flashcard>> GetFlashcardsAsync(int reviewerId) => _db.Table<Flashcard>().Where(c => c.ReviewerId == reviewerId).OrderBy(c => c.Order).ToListAsync();

    public Task<int> DeleteReviewerAsync(Reviewer reviewer) => _db.DeleteAsync(reviewer);
    public async Task<int> DeleteReviewerCascadeAsync(int reviewerId)
    {
        var cards = await GetFlashcardsAsync(reviewerId);
        foreach (var c in cards)
            await _db.DeleteAsync(c);
        return await _db.DeleteAsync(new Reviewer { Id = reviewerId });
    }

    public Task<int> DeleteFlashcardsForReviewerAsync(int reviewerId)
        => _db.ExecuteAsync("DELETE FROM Flashcard WHERE ReviewerId = ?", reviewerId);

    public Task<int> UpdateReviewerTitleAsync(int reviewerId, string newTitle)
        => _db.ExecuteAsync("UPDATE Reviewer SET Title = ? WHERE Id = ?", newTitle, reviewerId);
}
