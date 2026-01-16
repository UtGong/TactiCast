# TactiCast
Rrecommends viewpoints that make soccer tactics easier to understand by aligning visual perspective with tactical structure, moment-to-moment context, and player roles, while preserving visual continuity and user control.

# Football Tactic Board (Frontend)

A powerful, interactive web application for football coaches to design, animate, and manage tactical drills and match preparations. Built with **React**, **TypeScript**, and **Tailwind CSS**.

![Status](https://img.shields.io/badge/Status-Active-success)
![Tech](https://img.shields.io/badge/Stack-React%20%7C%20TypeScript%20%7C%20Tailwind-blue)

## 🌟 Features

* **Interactive Pitch:** 2D tactical board with drag-and-drop functionality for players and the ball.
* **Frame-based Animation:** Create sequences (steps) and watch smooth, interpolated animations of player movements (4s duration per step).
* **Roster Management:**
    * Default 11v11 setup (4-3-3 vs 4-4-2).
    * Add/Remove players dynamically.
    * Edit player numbers/labels directly in the UI.
* **Smart Ball Logic:** The ball automatically "snaps" to the nearest player when dragged close, assigning possession.
* **Save & Load System:**
    * Persist tactics to a local file system (via backend).
    * Review previously saved tactics.
    * Hot-swap tactics without reloading the page.
* **Playback Controls:** Play, Stop, and Step navigation.

## 🛠 Tech Stack

* **Core:** React 18, TypeScript
* **Build Tool:** Vite
* **Styling:** Tailwind CSS
* **Icons:** Lucide React
* **Graphics:** SVG (Scalable Vector Graphics) for the pitch and entities.

## 🚀 Getting Started

### Prerequisites

* Node.js (v16 or higher)
* npm or yarn
* *Note: This frontend requires the accompanying Node.js backend server running on port 3001 to handle file saving.*

### Installation

1.  **Clone the repository:**
    ```bash
    git clone <repository-url>
    cd football-tactic-board
    ```

2.  **Install dependencies:**
    ```bash
    npm install
    ```

3.  **Configure Environment (Optional):**
    Ensure `vite.config.ts` is set up to proxy API requests to the backend:
    ```ts
    server: {
      proxy: {
        '/api': {
          target: 'http://localhost:3001',
          changeOrigin: true,
          secure: false,
        }
      }
    }
    ```

### Running the App

1.  **Start the Backend (in a separate terminal):**
    ```bash
    node server.js
    ```

2.  **Start the Frontend:**
    ```bash
    npm run dev
    ```

3.  **Open in Browser:**
    Navigate to `http://localhost:5173` (or the URL shown in your terminal).

## 📖 Usage Guide

1.  **Designing a Play:**
    * Drag players to their starting positions for **Step 1**.
    * Click the **"+" (Add Frame)** button in the sequencer bar.
    * Move players/ball to their new positions for **Step 2**.
    * Repeat for as many steps as needed.

2.  **Animation:**
    * Click **Play** in the header. The system will interpolate movement between steps.
    * *Note: Player movement speed is calibrated to approx. 4 seconds per step.*

3.  **Saving Data:**
    * Click **Save**. This sends the data to the local server, which writes to `tactics_db.json`.
    * The board resets the timeline but keeps your current roster setup for the next drill.

4.  **Loading Data:**
    * Click **Load**. A modal will appear listing all saved tactics.
    * Select a tactic to load it onto the board.

## 📂 Project Structure

```text
src/
├── TacticBoard.tsx       # Main Application Logic (Board, Sequencer, State)
├── saved_tactics.json    # Initial/Fallback data source
├── index.css             # Tailwind imports
├── main.tsx              # React Entry point
└── App.tsx               # Root Component
