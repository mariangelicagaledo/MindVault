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

    // Delete only the flashcards for a given reviewer (used when saving an edited deck)
    public Task<int> DeleteFlashcardsForReviewerAsync(int reviewerId)
        => _db.ExecuteAsync("DELETE FROM Flashcard WHERE ReviewerId = ?", reviewerId);
}
