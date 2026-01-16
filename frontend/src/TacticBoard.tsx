import React, { useState, useEffect, useRef } from 'react';
import { Play, Pause, Plus, Trash2, UserPlus, Save, LayoutTemplate, FolderOpen, X, RotateCcw } from 'lucide-react';

// --- 1. TYPES ---
type PitchDims = { length: number; width: number };
const ROLES = ['GK', 'LB', 'CB', 'CB', 'RB', 'CDM', 'CM', 'CM', 'LW', 'ST', 'RW'];

interface Player {
  id: string;
  team: 'A' | 'B';
  label: string;
  role: string;
}

interface BallState {
  x: number;
  y: number;
  owner_id: string | null;
}

interface TacticFrame {
  id: string;
  player_pos: Record<string, [number, number]>;
  ball: BallState;
  note?: string;
}

interface TacticMetadata {
  tactic_id: string;
  title: string;
  pitch: PitchDims;
  teams: {
    A: { name: string; color: string };
    B: { name: string; color: string };
  };
  players: Player[];
  last_modified?: number;
}

interface SavedTacticData {
  meta: TacticMetadata;
  frames: TacticFrame[];
}

// --- 2. API SERVICE ---

const TacticService = {
  getAll: async (): Promise<SavedTacticData[]> => {
    try {
      const res = await fetch('/api/tactics');
      if (!res.ok) throw new Error('Failed to fetch');
      return await res.json();
    } catch (e) {
      console.error("API Error", e);
      return [];
    }
  },

  save: async (tactic: SavedTacticData) => {
    try {
      const res = await fetch('/api/tactics', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(tactic)
      });
      return await res.json();
    } catch (e) {
      console.error("API Save Error", e);
    }
  },

  delete: async (id: string) => {
    try {
      const res = await fetch(`/api/tactics/${id}`, { method: 'DELETE' });
      return await res.json();
    } catch (e) {
      console.error("API Delete Error", e);
      return [];
    }
  }
};

// --- 3. INITIAL DATA ---

const INITIAL_PITCH: PitchDims = { length: 105, width: 68 };

const generateDefaultRoster = () => {
  const players: Player[] = [];
  const startPos: Record<string, [number, number]> = {};

  // Team A (4-3-3)
  const teamA_formation = [[5, 34], [20, 10], [20, 26], [20, 42], [20, 58], [35, 34], [45, 20], [45, 48], [60, 10], [60, 34], [60, 58]];
  // Team B (4-4-2)
  const teamB_formation = [[100, 34], [85, 10], [85, 26], [85, 42], [85, 58], [75, 15], [75, 30], [75, 38], [75, 53], [65, 28], [65, 40]];

  teamA_formation.forEach((pos, i) => {
    const id = `A-${i}`;
    players.push({ id, team: 'A', label: `${i+1}`, role: ROLES[i] || 'SUB' });
    startPos[id] = [pos[0], pos[1]];
  });

  teamB_formation.forEach((pos, i) => {
    const id = `B-${i}`;
    players.push({ id, team: 'B', label: `${i+1}`, role: ROLES[i] || 'SUB' });
    startPos[id] = [pos[0], pos[1]];
  });

  return { players, startPos };
};

const defaultSetup = generateDefaultRoster();

// --- 4. COMPONENT ---

const TacticBoard = () => {
  // STATE
  const [metadata, setMetadata] = useState<TacticMetadata>({
    tactic_id: `tac_${Date.now()}`,
    title: "New Tactic",
    pitch: INITIAL_PITCH,
    teams: { A: { name: "Blue", color: "#3b82f6" }, B: { name: "Red", color: "#ef4444" } },
    players: defaultSetup.players
  });

  const [frames, setFrames] = useState<TacticFrame[]>([
    { id: 'frame-1', player_pos: defaultSetup.startPos, ball: { x: 50, y: 34, owner_id: null }, note: "Start" }
  ]);

  // UI STATE
  const [currentFrameIdx, setCurrentFrameIdx] = useState(0);
  const [selectedEntity, setSelectedEntity] = useState<string | null>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [isLoadModalOpen, setIsLoadModalOpen] = useState(false);
  const [savedTacticsList, setSavedTacticsList] = useState<SavedTacticData[]>([]);

  // ANIMATION REFS
  const [isPlaying, setIsPlaying] = useState(false);
  const [playbackProgress, setPlaybackProgress] = useState(0); 
  const requestRef = useRef<number>();
  const lastTimeRef = useRef<number>(0);
  const activeIdxRef = useRef<number>(0);
  const progressRef = useRef<number>(0);

  // --- ACTIONS ---

  const handleSaveTactic = async () => {
    const dataToSave: SavedTacticData = {
      meta: { ...metadata, last_modified: Date.now() },
      frames: frames
    };
    
    // Call Backend
    await TacticService.save(dataToSave);
    alert("Tactic saved to Server!");

    // Reset Workflow (Keep Roster, Reset Timeline)
    const newId = `tac_${Date.now()}`;
    // Use current ending positions as the start for the next one
    const startPositions = frames[currentFrameIdx] ? frames[currentFrameIdx].player_pos : defaultSetup.startPos;

    setMetadata(prev => ({
      ...prev,
      tactic_id: newId,
      title: "New Tactic"
    }));

    setFrames([{
      id: `frame-${Date.now()}`,
      player_pos: startPositions,
      ball: { x: 50, y: 34, owner_id: null },
      note: "Start"
    }]);
    
    setCurrentFrameIdx(0);
  };

  const resetRosterToDefault = () => {
    if(!window.confirm("Reset entire roster to default 11v11 positions?")) return;
    const fresh = generateDefaultRoster();
    setMetadata(prev => ({ ...prev, players: fresh.players }));
    setFrames(prev => {
        const newFrames = [...prev];
        if (newFrames[currentFrameIdx]) {
            newFrames[currentFrameIdx] = {
                ...newFrames[currentFrameIdx],
                player_pos: fresh.startPos
            };
        }
        return newFrames;
    });
  };

  const loadTactic = (tactic: SavedTacticData) => {
    if (window.confirm("Load this tactic? Unsaved changes will be lost.")) {
      setMetadata(tactic.meta);
      setFrames(tactic.frames);
      setCurrentFrameIdx(0);
      setIsLoadModalOpen(false);
    }
  };

  const deleteSavedTactic = async (id: string, e: React.MouseEvent) => {
    e.stopPropagation();
    if (window.confirm("Permanently delete this saved tactic?")) {
      const updated = await TacticService.delete(id);
      setSavedTacticsList(updated);
    }
  };

  const openLoadModal = async () => {
    const data = await TacticService.getAll();
    setSavedTacticsList(data);
    setIsLoadModalOpen(true);
  };

  // --- EDITOR HELPERS ---
  const addPlayer = (team: 'A' | 'B') => {
    const newId = `${team}-${Date.now()}`;
    const newPlayer: Player = { id: newId, team, label: '?', role: 'SUB' };
    setMetadata(prev => ({ ...prev, players: [...prev.players, newPlayer] }));
    const defaultX = team === 'A' ? 5 : 100;
    setFrames(prev => prev.map(f => ({ ...f, player_pos: { ...f.player_pos, [newId]: [defaultX, 65] } })));
  };
  const updatePlayerLabel = (id: string, val: string) => setMetadata(prev => ({ ...prev, players: prev.players.map(p => p.id === id ? { ...p, label: val } : p) }));
  const removePlayer = (pid: string) => setMetadata(prev => ({ ...prev, players: prev.players.filter(p => p.id !== pid) }));
  
  const addNewFrame = () => {
    const lastFrame = frames[frames.length - 1];
    if (!lastFrame) return;
    setFrames([...frames, { id: `frame-${Date.now()}`, player_pos: { ...lastFrame.player_pos }, ball: { ...lastFrame.ball }, note: "" }]);
    setCurrentFrameIdx(frames.length);
  };

  const deleteFrame = (idx: number) => {
    if (frames.length <= 1) return;
    setFrames(frames.filter((_, i) => i !== idx));
    if (currentFrameIdx >= idx) setCurrentFrameIdx(Math.max(0, currentFrameIdx - 1));
  };

  // --- INTERACTION ---
  const handleDragStart = (id: string, e: React.MouseEvent) => { if (isPlaying) return; e.stopPropagation(); setSelectedEntity(id); setIsDragging(true); };
  const handleMouseMove = (e: React.MouseEvent<SVGSVGElement>) => {
    if (!isDragging || !selectedEntity || isPlaying) return;
    const svg = e.currentTarget; const pt = svg.createSVGPoint(); pt.x = e.clientX; pt.y = e.clientY;
    const { x, y } = pt.matrixTransform(svg.getScreenCTM()?.inverse());
    const cx = Math.max(0, Math.min(metadata.pitch.length, x)); const cy = Math.max(0, Math.min(metadata.pitch.width, y));
    setFrames(prev => {
      const newFrames = [...prev]; 
      const frame = { ...newFrames[currentFrameIdx] };
      if (!frame) return prev; // Safety
      if (selectedEntity === 'ball') {
        let owner: string | null = null; let finalX = cx, finalY = cy;
        const nearest = metadata.players.find(p => { const [px, py] = frame.player_pos[p.id] || [0,0]; return Math.hypot(px - cx, py - cy) < 3; });
        if (nearest) { owner = nearest.id; const [px, py] = frame.player_pos[nearest.id]; finalX = px + 1; finalY = py + 1; }
        frame.ball = { x: finalX, y: finalY, owner_id: owner };
      } else {
        frame.player_pos = { ...frame.player_pos, [selectedEntity]: [cx, cy] };
        if (frame.ball.owner_id === selectedEntity) frame.ball = { ...frame.ball, x: cx + 1, y: cy + 1 };
      }
      newFrames[currentFrameIdx] = frame; return newFrames;
    });
  };

  // --- ANIMATION ENGINE ---
  const togglePlay = () => {
    if (isPlaying) { setIsPlaying(false); } else {
      if (currentFrameIdx >= frames.length - 1) { setCurrentFrameIdx(0); activeIdxRef.current = 0; progressRef.current = 0; }
      else { activeIdxRef.current = currentFrameIdx; progressRef.current = 0; }
      lastTimeRef.current = performance.now(); setIsPlaying(true);
    }
  };

  useEffect(() => {
    if (!isPlaying) { cancelAnimationFrame(requestRef.current!); return; }
    const animate = (time: number) => {
      const delta = time - lastTimeRef.current; lastTimeRef.current = time;
      
      const SLIDE_DURATION = 4000; 
      
      progressRef.current += delta / SLIDE_DURATION;

      if (progressRef.current >= 1) {
        if (activeIdxRef.current < frames.length - 1) { 
            activeIdxRef.current += 1; 
            progressRef.current = 0; 
            setCurrentFrameIdx(activeIdxRef.current); 
        }
        else { 
            setIsPlaying(false); 
            setPlaybackProgress(1); 
            return; 
        }
      }
      setPlaybackProgress(progressRef.current);
      requestRef.current = requestAnimationFrame(animate);
    };
    requestRef.current = requestAnimationFrame(animate);
    return () => cancelAnimationFrame(requestRef.current!);
  }, [isPlaying, frames.length]);

  // --- RENDER CALCULATION (CRASH FIX APPLIED) ---
  const getRenderState = () => {
    const idx = currentFrameIdx; 
    
    // --- CRASH FIX: Ensure current frame exists ---
    const currentFrame = frames[idx];
    if (!currentFrame) return { players: {}, ball: { x: 50, y: 34, owner_id: null } };

    // Static mode
    if (!isPlaying || idx >= frames.length - 1) {
        return { players: currentFrame.player_pos, ball: currentFrame.ball };
    }
    
    // --- CRASH FIX: Ensure next frame exists ---
    const nextFrame = frames[idx + 1]; 
    if (!nextFrame) return { players: currentFrame.player_pos, ball: currentFrame.ball };

    const t = playbackProgress; 
    const interpPlayers: Record<string, [number, number]> = {};
    metadata.players.forEach(p => { 
        const s = currentFrame.player_pos[p.id] || [0,0]; 
        const e = nextFrame.player_pos[p.id] || s; 
        interpPlayers[p.id] = [s[0] + (e[0] - s[0]) * t, s[1] + (e[1] - s[1]) * t]; 
    });
    
    const bs = currentFrame.ball; 
    const be = nextFrame.ball;
    return { players: interpPlayers, ball: { x: bs.x + (be.x - bs.x) * t, y: bs.y + (be.y - bs.y) * t, owner_id: null } };
  };
  
  const renderState = getRenderState();

  return (
    <div className="flex flex-col h-screen bg-slate-50 text-slate-800 font-sans select-none relative">
      
      {/* LOAD MODAL */}
      {isLoadModalOpen && (
        <div className="absolute inset-0 z-50 bg-black/60 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl w-full max-w-lg overflow-hidden flex flex-col max-h-[80vh]">
            <div className="p-4 border-b flex justify-between items-center bg-slate-50">
              <h2 className="font-bold text-lg flex items-center gap-2"><FolderOpen size={20} className="text-blue-600"/> Saved Tactics</h2>
              <button onClick={() => setIsLoadModalOpen(false)} className="p-1 hover:bg-slate-200 rounded-full"><X size={20}/></button>
            </div>
            <div className="overflow-y-auto p-2">
              {savedTacticsList.length === 0 && <div className="text-center p-8 text-slate-400">No saved tactics found.</div>}
              {savedTacticsList.map((tactic) => (
                <div key={tactic.meta.tactic_id} onClick={() => loadTactic(tactic)} className="flex justify-between items-center p-4 hover:bg-blue-50 border-b border-slate-100 cursor-pointer group transition">
                  <div>
                    <div className="font-bold text-slate-800">{tactic.meta.title}</div>
                    <div className="text-xs text-slate-400">{new Date(tactic.meta.last_modified || 0).toLocaleString()}</div>
                  </div>
                  <button onClick={(e) => deleteSavedTactic(tactic.meta.tactic_id, e)} className="p-2 text-slate-300 hover:text-red-500 hover:bg-red-50 rounded transition"><Trash2 size={16}/></button>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* HEADER */}
      <header className="bg-white border-b px-6 py-4 flex justify-between items-center z-20 shadow-sm">
        <div className="flex items-center gap-3">
            <div className="bg-blue-100 p-2 rounded-lg"><LayoutTemplate size={20} className="text-blue-600"/></div>
            <div>
              <input className="font-bold text-lg focus:outline-none focus:border-b-2 focus:border-blue-500 w-full bg-transparent" value={metadata.title} onChange={(e) => setMetadata({...metadata, title: e.target.value})} />
              <p className="text-xs text-slate-400">Match Prep • 11v11</p>
            </div>
        </div>
        <div className="flex gap-3">
           <button onClick={openLoadModal} className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-slate-600 hover:text-blue-600 bg-slate-100 hover:bg-blue-50 rounded-lg transition"><FolderOpen size={16}/> Load</button>
           <button onClick={handleSaveTactic} className="flex items-center gap-2 px-4 py-2 text-sm font-medium text-slate-600 hover:text-slate-900 bg-slate-100 hover:bg-slate-200 rounded-lg transition"><Save size={16}/> Save</button>
           <button onClick={togglePlay} className={`flex items-center gap-2 px-6 py-2 rounded-lg font-bold text-white shadow-sm transition-all active:scale-95 ${isPlaying ? 'bg-red-500' : 'bg-blue-600 hover:bg-blue-700'}`}>{isPlaying ? <><Pause size={18}/> Stop</> : <><Play size={18}/> Play</>}</button>
        </div>
      </header>

      {/* BODY */}
      <div className="flex flex-1 overflow-hidden">
        {/* SIDEBAR */}
        <aside className="w-72 bg-white border-r flex flex-col overflow-hidden z-10">
          <div className="p-4 border-b bg-white flex justify-between items-center sticky top-0">
             <h2 className="text-xs font-bold text-slate-400 uppercase tracking-wider">Squad List</h2>
             <button onClick={resetRosterToDefault} className="text-xs text-blue-500 hover:text-blue-700 flex items-center gap-1 hover:bg-blue-50 px-2 py-1 rounded" title="Reset Players"><RotateCcw size={12}/> Reset</button>
          </div>
          <div className="flex-1 overflow-y-auto p-3 space-y-8 custom-scrollbar">
            {['A', 'B'].map(teamKey => {
               const team = teamKey === 'A' ? metadata.teams.A : metadata.teams.B;
               return (
                <div key={teamKey}>
                  <div className="flex justify-between items-center px-1 mb-3">
                    <span className={`font-bold text-sm flex items-center gap-2`}><div className="w-2 h-2 rounded-full" style={{background: team.color}}/>{team.name}</span>
                    <button onClick={() => addPlayer(teamKey as 'A'|'B')} className="p-1 hover:bg-slate-50 text-slate-400 hover:text-blue-600 rounded"><UserPlus size={16}/></button>
                  </div>
                  <ul className="space-y-2">
                    {metadata.players.filter(p => p.team === teamKey).map(p => (
                       <li key={p.id} className={`group flex justify-between items-center p-2 rounded-lg border transition-all ${selectedEntity === p.id ? 'bg-blue-50 border-blue-200 shadow-sm' : 'bg-white border-transparent hover:border-slate-100'}`} onClick={() => setSelectedEntity(p.id)}>
                         <div className="flex gap-3 items-center">
                           <input value={p.label} onChange={(e) => updatePlayerLabel(p.id, e.target.value)} className="w-8 h-8 rounded-full text-center text-xs font-bold shadow-sm focus:outline-none focus:ring-2 focus:ring-blue-400" style={{backgroundColor: team.color, color: 'white'}} onClick={(e) => e.stopPropagation()} />
                           <div className="flex flex-col">
                              <span className="text-xs font-bold text-slate-700">{p.role}</span>
                              <span className="text-[10px] text-slate-400">ID: {p.id.slice(-4)}</span>
                           </div>
                         </div>
                         <button onClick={(e) => { e.stopPropagation(); removePlayer(p.id); }} className="text-slate-300 hover:text-red-500 p-1 opacity-0 group-hover:opacity-100 transition"><Trash2 size={14}/></button>
                       </li>
                    ))}
                  </ul>
                </div>
               );
            })}
          </div>
        </aside>

        {/* CANVAS */}
        <main className="flex-1 flex flex-col bg-slate-50 relative">
          <div className="flex-1 flex items-center justify-center p-8 overflow-hidden">
             <div className="relative shadow-xl rounded-xl overflow-hidden border-4 border-white bg-green-700 transition-all" style={{ width: '100%', maxWidth: '900px', aspectRatio: '105/68' }}>
                <svg viewBox={`0 0 ${metadata.pitch.length} ${metadata.pitch.width}`} className="w-full h-full cursor-crosshair" onMouseMove={handleMouseMove} onMouseUp={() => setIsDragging(false)} onMouseLeave={() => setIsDragging(false)}>
                  <rect width="100%" height="100%" fill="#15803d" />
                  <pattern id="grass" width="10" height="10" patternUnits="userSpaceOnUse"><rect width="10" height="10" fill="#166534" fillOpacity="0.2"/></pattern>
                  <rect width="100%" height="100%" fill="url(#grass)" />
                  <g stroke="rgba(255,255,255,0.7)" strokeWidth="0.5" fill="none">
                    <rect x="0" y="0" width="105" height="68" />
                    <line x1="52.5" y1="0" x2="52.5" y2="68" /><circle cx="52.5" cy="34" r="9.15" />
                    <rect x="0" y="13.84" width="16.5" height="40.32" /><rect x="88.5" y="13.84" width="16.5" height="40.32" />
                    <rect x="-2" y="30.34" width="2" height="7.32" stroke="none" fill="rgba(0,0,0,0.2)"/><rect x="105" y="30.34" width="2" height="7.32" stroke="none" fill="rgba(0,0,0,0.2)"/>
                  </g>
                  {metadata.players.map(p => {
                    // --- CRASH FIX: Default if player pos missing ---
                    const [x, y] = renderState.players[p.id] || [0,0];
                    const color = p.team === 'A' ? metadata.teams.A.color : metadata.teams.B.color;
                    const isSel = selectedEntity === p.id;
                    return (
                      <g key={p.id} transform={`translate(${x},${y})`} style={{ cursor: isPlaying ? 'default' : 'move' }} onMouseDown={(e) => handleDragStart(p.id, e)} className="transition-transform">
                        <ellipse cx="0" cy="2" rx="2" ry="1" fill="rgba(0,0,0,0.4)" />
                        <circle r="2.2" fill={color} stroke="white" strokeWidth="0.3" className="drop-shadow-sm"/>
                        <text y="0.8" fontSize="1.6" textAnchor="middle" fill="white" fontWeight="800" pointerEvents="none" style={{ textShadow: '0px 1px 1px rgba(0,0,0,0.5)' }}>{p.label}</text>
                        {isSel && !isPlaying && <circle r="3.5" stroke="yellow" strokeWidth="0.4" fill="none" strokeDasharray="1,0.5" opacity="0.8" />}
                      </g>
                    );
                  })}
                  <g transform={`translate(${renderState.ball.x}, ${renderState.ball.y})`} style={{ cursor: isPlaying ? 'default' : 'move' }} onMouseDown={(e) => handleDragStart('ball', e)}>
                    <circle r="1.1" fill="white" stroke="#111" strokeWidth="0.2" className="drop-shadow-md" />
                  </g>
                </svg>
                {isPlaying && <div className="absolute top-4 right-4 bg-black/70 text-white px-4 py-2 rounded-lg text-sm backdrop-blur-md font-mono border border-white/10 shadow-lg">Sequencing... {Math.round(playbackProgress * 100)}%</div>}
             </div>
          </div>
          {/* SEQUENCER */}
          <div className="h-28 bg-white border-t px-8 py-3 flex flex-col justify-center shadow-[0_-10px_40px_-15px_rgba(0,0,0,0.1)] z-20">
            <div className="flex items-center justify-between mb-2">
               <h3 className="text-xs font-bold text-slate-400 uppercase tracking-wider">Sequencer</h3>
               <button onClick={() => deleteFrame(currentFrameIdx)} className="text-xs text-red-400 hover:text-red-600 flex items-center gap-1 hover:bg-red-50 px-2 py-1 rounded transition" disabled={frames.length <= 1}><Trash2 size={12}/> Remove Step</button>
            </div>
            <div className="flex items-center gap-3 overflow-x-auto pb-2 custom-scrollbar">
              {frames.map((f, i) => {
                const isActive = currentFrameIdx === i;
                return (
                  <button key={f.id} onClick={() => { setCurrentFrameIdx(i); setIsPlaying(false); setPlaybackProgress(0); }} className={`relative group flex flex-col items-center justify-between min-w-[70px] h-[54px] p-2 rounded-lg border-2 transition-all duration-200 ${isActive ? 'bg-white border-blue-500 shadow-md ring-2 ring-blue-50 z-10' : 'bg-white border-slate-100 text-slate-400 hover:border-blue-200 hover:shadow-sm hover:-translate-y-0.5'}`}>
                    <span className={`text-xs font-bold ${isActive ? 'text-blue-600' : 'text-slate-400'}`}>Step {i + 1}</span>
                    <div className="flex gap-1">
                       {[1,2,3].map(d => <div key={d} className={`w-1 h-1 rounded-full ${isActive ? 'bg-blue-500' : 'bg-slate-200'}`}/>)}
                    </div>
                  </button>
                )
              })}
              <button onClick={addNewFrame} className="flex items-center justify-center min-w-[54px] h-[54px] rounded-lg border-2 border-dashed border-slate-300 text-slate-300 hover:border-blue-400 hover:text-blue-500 hover:bg-blue-50 transition-all group"><Plus size={20} className="group-hover:scale-110 transition-transform"/></button>
            </div>
          </div>
        </main>
      </div>
    </div>
  );
};

export default TacticBoard;