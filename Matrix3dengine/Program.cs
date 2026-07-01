using System;
using System.Threading;
using System.Collections.Generic;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Threading.Tasks;

// 3D Player Avatar / Anchor Geometry 
float[][] blockVerts = new float[][] {
    new float[] {-1,-1,-1}, new float[] {1,-1,-1}, new float[] {1,1,-1}, new float[] {-1,1,-1},
    new float[] {-1,-1,1}, new float[] {1,-1,1}, new float[] {1,1,1}, new float[] {-1,1,1}
};
int[][] blockEdges = new int[][] {
    new int[] {0,1}, new int[] {1,2}, new int[] {2,3}, new int[] {3,0},
    new int[] {4,5}, new int[] {5,6}, new int[] {6,7}, new int[] {7,4},
    new int[] {0,4}, new int[] {1,5}, new int[] {2,6}, new int[] {3,7}
};

// Camera / Engine Settings
float camX = 0, camY = 0, camZ = -5;
float camRotX = 0, camRotY = 0;
int width = 80, height = 40;

// Theme Colors (White Background Setup)
const string RESET = "\x1b[0m";
const string BG_WHITE = "\x1b[47m";   
const string FG_BLACK = "\x1b[30m";   
const string FG_BLUE = "\x1b[34m";    
const string FG_RED = "\x1b[31m";     
const string FG_MAGENTA = "\x1b[35m"; 

// Networking Variables
StreamWriter? networkWriter = null;
int myPlayerId = -1;
Dictionary<int, float[]> remotePlayers = new Dictionary<int, float[]>();
Dictionary<int, string> playerMessages = new Dictionary<int, string>();
object networkLock = new object();

string currentLocalMessage = "";

// Track mouse positioning
int lastMouseX = -1;
int lastMouseY = -1;

Console.Clear();
Console.WriteLine("=== MATRIX 3D ONE-ANCHOR INFINITE CANVAS ===");
Console.Write("Type 'h' to HOST a private server or 'j' to JOIN a friend: ");
string choice = Console.ReadLine()?.ToLower() ?? "h";

if (choice == "h")
{
    myPlayerId = 1;
    Task.Run(() => StartServerLoop());
    ConnectToServer("127.0.0.1");
}
else
{
    Console.Write("Enter Server IP: ");
    string ip = Console.ReadLine() ?? "127.0.0.1";
    ConnectToServer(ip);
}

Console.CursorVisible = false;
Console.OutputEncoding = Encoding.UTF8;

// Main Engine Loop
while (true)
{
    // --- MOUSE TRACKING LOOK CONTROL ---
    try
    {
        int mouseX = Console.CursorLeft;
        int mouseY = Console.CursorTop;
        if (lastMouseX != -1 && (mouseX != lastMouseX || mouseY != lastMouseY))
        {
            camRotY += (mouseX - lastMouseX) * 0.01f;
            camRotX += (mouseY - lastMouseY) * 0.01f;
        }
        lastMouseX = mouseX;
        lastMouseY = mouseY;
    } 
    catch {}

    if (Console.KeyAvailable)
    {
        ConsoleKey key = Console.ReadKey(true).Key;
        
        if (key == ConsoleKey.Enter)
        {
            Console.SetCursorPosition(0, height + 1);
            Console.Write(RESET + "Enter Chat or Command (/kill, /tp x y z):             ");
            Console.SetCursorPosition(43, height + 1);
            Console.CursorVisible = true;
            string? input = Console.ReadLine()?.Trim();
            Console.CursorVisible = false;
            
            if (!string.IsNullOrEmpty(input))
            {
                if (input.ToLower() == "/kill")
                {
                    camX = 0; camY = 0; camZ = -5;
                    camRotX = 0; camRotY = 0;
                    currentLocalMessage = "* Wasted *";
                }
                else if (input.ToLower().StartsWith("/tp "))
                {
                    try
                    {
                        string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 4)
                        {
                            camX = float.Parse(parts[1]);
                            camY = float.Parse(parts[2]);
                            camZ = float.Parse(parts[3]);
                            currentLocalMessage = $"Teleported to {camX}, {camY}, {camZ}";
                        }
                    }
                    catch { currentLocalMessage = "Invalid /tp arguments!"; }
                }
                else
                {
                    currentLocalMessage = input.Replace(",", " ").Replace("|", " ").Replace(":", " ");
                }

                string trackingMsg = currentLocalMessage;
                Task.Run(async () => {
                    await Task.Delay(4000);
                    if (currentLocalMessage == trackingMsg) currentLocalMessage = "";
                    SendNetworkUpdate();
                });
            }
            SendNetworkUpdate();
        }
        else
        {
            // Calculate looking directions for true relative movement matrix
            float forwardX = MathF.Sin(camRotY);
            float forwardZ = MathF.Cos(camRotY);
            float rightX = MathF.Cos(camRotY);
            float rightZ = -MathF.Sin(camRotY);

            if (key == ConsoleKey.W) { camX += forwardX * 0.5f; camZ += forwardZ * 0.5f; }
            if (key == ConsoleKey.S) { camX -= forwardX * 0.5f; camZ -= forwardZ * 0.5f; }
            if (key == ConsoleKey.A) { camX -= rightX * 0.5f; camZ -= rightZ * 0.5f; }
            if (key == ConsoleKey.D) { camX += rightX * 0.5f; camZ += rightZ * 0.5f; }
            
            if (key == ConsoleKey.LeftArrow)  camRotY -= 0.06f;
            if (key == ConsoleKey.RightArrow) camRotY += 0.06f;
            if (key == ConsoleKey.UpArrow)    camRotX -= 0.06f;
            if (key == ConsoleKey.DownArrow)  camRotX += 0.06f;
            
            SendNetworkUpdate();
        }
    }

    char[,] charBuffer = new char[width, height];
    string[,] colorBuffer = new string[width, height];

    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            charBuffer[x, y] = ' '; 
            colorBuffer[x, y] = BG_WHITE + FG_BLACK;
        }
    }

    // Dynamic horizon ground line
    int horizonY = height / 2 + (int)(camY * 10f - camRotX * 20f);
    if (horizonY >= 0 && horizonY < height)
    {
        for (int x = 0; x < width; x++)
        {
            charBuffer[x, horizonY] = '_';
            colorBuffer[x, horizonY] = BG_WHITE + FG_BLACK;
        }
    }

    // --- THE SINGLE REFERENCE SQUARE ---
    // Renders one steady blue center box at coordinates (0, 0, 0)
    RenderObject(blockVerts, blockEdges, 0f, 0f, 0f, charBuffer, colorBuffer, FG_BLUE, "[Spawn Point]");

    // Render other connected multiplayer players (Red Boxes)
    lock (networkLock)
    {
        foreach (var p in remotePlayers)
        {
            if (p.Key == myPlayerId) continue;
            float[] pos = p.Value;
            string msg = playerMessages.ContainsKey(p.Key) ? playerMessages[p.Key] : "";
            RenderObject(blockVerts, blockEdges, pos[0], pos[1], pos[2], charBuffer, colorBuffer, FG_RED, msg);
        }
    }

    // Coordinates HUD overlay
    string status = $"XYZ: {camX:0.0}, {camY:0.0}, {camZ:0.0} | Players Online: {remotePlayers.Count + 1}";
    for (int i = 0; i < status.Length && i < width; i++)
    {
        charBuffer[i, 0] = status[i];
        colorBuffer[i, 0] = BG_WHITE + FG_BLACK;
    }

    string prompt = "[Mouse/Arrows = Look] | [WASD = Relative Move] | [Enter = Command]";
    for (int i = 0; i < prompt.Length && i < width; i++) 
    {
        charBuffer[i, height - 1] = prompt[i];
        colorBuffer[i, height - 1] = BG_WHITE + FG_BLACK;
    }

    RenderToScreen(charBuffer, colorBuffer);
    Thread.Sleep(33);
}

void SendNetworkUpdate()
{
    try 
    { 
        string msgToSend = string.IsNullOrEmpty(currentLocalMessage) ? "NONE" : currentLocalMessage;
        networkWriter?.WriteLine($"{camX},{camY},{camZ},{msgToSend}"); 
    } 
    catch { }
}

void ConnectToServer(string targetIp)
{
    try
    {
        TcpClient client = new TcpClient(targetIp, 12345);
        NetworkStream stream = client.GetStream();
        StreamReader reader = new StreamReader(stream);
        networkWriter = new StreamWriter(stream) { AutoFlush = true };

        Task.Run(() => {
            while (true)
            {
                try
                {
                    string? line = reader.ReadLine();
                    if (string.IsNullOrEmpty(line)) continue;

                    if (line.StartsWith("ID:"))
                    {
                        myPlayerId = int.Parse(line.Split(':')[1]);
                        continue;
                    }

                    lock (networkLock)
                    {
                        remotePlayers.Clear();
                        playerMessages.Clear();
                        string[] playerPackets = line.Split('|');
                        foreach (var player in playerPackets)
                        {
                            if (string.IsNullOrEmpty(player)) continue;
                            string[] parts = player.Split(':');
                            int id = int.Parse(parts[0]);
                            string[] data = parts[1].Split(',');

                            remotePlayers[id] = new float[] {
                                float.Parse(data[0]),
                                float.Parse(data[1]),
                                float.Parse(data[2])
                            };
                            
                            if (data.Length > 3 && data[3] != "NONE")
                            {
                                playerMessages[id] = data[3];
                            }
                        }
                    }
                }
                catch { break; }
            }
        });
    }
    catch
    {
        Console.WriteLine("Connection closed.");
        Environment.Exit(0);
    }
}

void StartServerLoop()
{
    System.Collections.Concurrent.ConcurrentDictionary<int, string> serverPlayerPositions = new();
    System.Collections.Concurrent.ConcurrentDictionary<int, StreamWriter> serverWriters = new();
    int idCounter = 0;

    TcpListener listener = new TcpListener(IPAddress.Any, 12345);
    listener.Start();

    while (true)
    {
        TcpClient client = listener.AcceptTcpClient();
        int assignedId = Interlocked.Increment(ref idCounter);
        
        Task.Run(() => {
            using NetworkStream stream = client.GetStream();
            using StreamReader reader = new StreamReader(stream);
            using StreamWriter writer = new StreamWriter(stream) { AutoFlush = true };

            serverWriters[assignedId] = writer;
            serverPlayerPositions[assignedId] = "0,0,-5,NONE";
            writer.WriteLine($"ID:{assignedId}");

            try
            {
                while (!reader.EndOfStream)
                {
                    string? msg = reader.ReadLine();
                    if (string.IsNullOrEmpty(msg)) continue;
                    serverPlayerPositions[assignedId] = msg;

                    List<string> packet = new();
                    foreach (var p in serverPlayerPositions) packet.Add($"{p.Key}:{p.Value}");
                    string fullPacket = string.Join("|", packet);

                    foreach (var w in serverWriters.Values) { try { w.WriteLine(fullPacket); } catch { } }
                }
            }
            catch { }
            finally
            {
                serverWriters.TryRemove(assignedId, out _);
                serverPlayerPositions.TryRemove(assignedId, out _);
            }
        });
    }
}

void RenderObject(float[][] verts, int[][] lines, float ox, float oy, float oz, char[,] cBuf, string[,] colBuf, string color, string overheadMsg)
{
    int[][] proj = new int[verts.Length][];
    float avgScreenX = 0;
    float highestY = 9999; 

    for (int i = 0; i < verts.Length; i++)
    {
        float cx = verts[i][0] + ox - camX;
        float cy = verts[i][1] + oy - camY;
        float cz = verts[i][2] + oz - camZ;

        float cosY = MathF.Cos(-camRotY), sinY = MathF.Sin(-camRotY);
        float rx1 = cx * cosY + cz * sinY;
        float rz1 = -cx * sinY + cz * cosY;

        float cosX = MathF.Cos(-camRotX), sinX = MathF.Sin(-camRotX);
        float ry2 = cy * cosX - rz1 * sinX;
        float rz2 = cy * sinX + rz1 * cosX;

        if (rz2 <= 0.1f) return; 

        int sx = (int)(width / 2 + (rx1 * 50.0f / rz2) * 2.0f);
        int sy = (int)(height / 2 + (ry2 * 50.0f / rz2));
        proj[i] = new int[] { sx, sy };

        avgScreenX += sx;
        if (sy < highestY) highestY = sy;
    }
    
    avgScreenX /= verts.Length;

    foreach (var edge in lines) DrawLine(proj[edge[0]][0], proj[edge[0]][1], proj[edge[1]][0], proj[edge[1]][1], cBuf, colBuf, color);

    if (!string.IsNullOrEmpty(overheadMsg))
    {
        int textX = (int)avgScreenX - (overheadMsg.Length / 2);
        int textY = (int)highestY - 2; 

        if (textY >= 0 && textY < height)
        {
            for (int i = 0; i < overheadMsg.Length; i++)
            {
                int targetX = textX + i;
                if (targetX >= 0 && targetX < width)
                {
                    cBuf[targetX, textY] = overheadMsg[i];
                    colBuf[targetX, textY] = BG_WHITE + FG_MAGENTA; 
                }
            }
        }
    }
}

void DrawLine(int x0, int y0, int x1, int y1, char[,] cBuf, string[,] colBuf, string color)
{
    int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
    int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
    int err = dx + dy, e2;
    while (true)
    {
        if (x0 >= 0 && x0 < width && y0 >= 0 && y0 < height) { cBuf[x0, y0] = '#'; colBuf[x0, y0] = color; }
        if (x0 == x1 && y0 == y1) break;
        e2 = 2 * err;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
    }
}

void RenderToScreen(char[,] cBuf, string[,] colBuf)
{
    Console.SetCursorPosition(0, 0);
    StringBuilder sb = new StringBuilder();
    string activeColor = "";
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            if (colBuf[x, y] != activeColor) { activeColor = colBuf[x, y]; sb.Append(activeColor); }
            sb.Append(cBuf[x, y]);
        }
        sb.Append('\n');
    }
    Console.Write(sb.ToString());
}
