import express from 'express';
import cors from 'cors';
import fs from 'fs/promises';
import path from 'path';
import { fileURLToPath } from 'url';

const app = express();
const PORT = 3001; // Backend runs on 3001

// Middleware
app.use(cors());
app.use(express.json());

// Path to your data file
const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);
const DATA_FILE = path.join(__dirname, 'tactics_db.json');

// Helper: Ensure DB file exists
const initDb = async () => {
  try {
    await fs.access(DATA_FILE);
  } catch {
    await fs.writeFile(DATA_FILE, '[]'); // Create empty array if missing
  }
};

// API: Get All Tactics
app.get('/api/tactics', async (req, res) => {
  await initDb();
  try {
    const data = await fs.readFile(DATA_FILE, 'utf-8');
    res.json(JSON.parse(data));
  } catch (err) {
    res.status(500).json({ error: 'Failed to read database' });
  }
});

// API: Save a Tactic (Create or Update)
app.post('/api/tactics', async (req, res) => {
  await initDb();
  try {
    const newTactic = req.body;
    const dataRaw = await fs.readFile(DATA_FILE, 'utf-8');
    let db = JSON.parse(dataRaw);

    // Check if exists
    const index = db.findIndex(t => t.meta.tactic_id === newTactic.meta.tactic_id);
    if (index >= 0) {
      db[index] = newTactic; // Update
    } else {
      db.push(newTactic); // Insert
    }

    await fs.writeFile(DATA_FILE, JSON.stringify(db, null, 2));
    res.json({ success: true, db });
  } catch (err) {
    res.status(500).json({ error: 'Failed to save tactic' });
  }
});

// API: Delete a Tactic
app.delete('/api/tactics/:id', async (req, res) => {
  await initDb();
  try {
    const { id } = req.params;
    const dataRaw = await fs.readFile(DATA_FILE, 'utf-8');
    let db = JSON.parse(dataRaw);
    
    db = db.filter(t => t.meta.tactic_id !== id);
    
    await fs.writeFile(DATA_FILE, JSON.stringify(db, null, 2));
    res.json(db);
  } catch (err) {
    res.status(500).json({ error: 'Failed to delete tactic' });
  }
});

app.listen(PORT, () => {
  console.log(`Server running at http://localhost:${PORT}`);
});