namespace CloudGame.Gameplay
{
    /// <summary>
    /// Parámetros de un nivel: tamaño del tablero, minas y tiempo por turno.
    /// </summary>
    public readonly struct LevelDefinition
    {
        public LevelDefinition(int level, int size, int minMines, int maxMines, float turnSeconds)
        {
            Level = level;
            Size = size;
            MinMines = minMines;
            MaxMines = maxMines;
            TurnSeconds = turnSeconds;
        }

        public int Level { get; }

        public int Size { get; }

        public int MinMines { get; }

        public int MaxMines { get; }

        public float TurnSeconds { get; }

        public int CellCount => Size * Size;

        public override string ToString()
        {
            return $"N{Level}: {Size}x{Size}, {MinMines}-{MaxMines} minas, {TurnSeconds}s";
        }
    }
}
