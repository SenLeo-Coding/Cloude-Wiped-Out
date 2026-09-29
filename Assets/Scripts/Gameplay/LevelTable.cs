using System;

namespace CloudGame.Gameplay
{
    /// <summary>
    /// Progresión de niveles acordada. Los niveles 1-12 están fijados por diseño;
    /// a partir del 13 la tabla continúa con una fórmula para que no se agote.
    /// </summary>
    public static class LevelTable
    {
        private const int LastFixedLevel = 12;
        private const int MaxBoardSize = 12;

        private static readonly LevelDefinition[] Fixed =
        {
            new LevelDefinition(1, 2, 2, 2, 5f),
            new LevelDefinition(2, 3, 2, 3, 5f),
            new LevelDefinition(3, 3, 3, 4, 4f),
            new LevelDefinition(4, 4, 4, 5, 4f),
            new LevelDefinition(5, 4, 5, 6, 3f),
            new LevelDefinition(6, 5, 6, 7, 3f),
            new LevelDefinition(7, 5, 7, 8, 2.5f),
            new LevelDefinition(8, 6, 8, 9, 2.5f),
            new LevelDefinition(9, 6, 9, 11, 2f),
            new LevelDefinition(10, 7, 11, 13, 2f),
            new LevelDefinition(11, 7, 13, 15, 2f),
            new LevelDefinition(12, 8, 15, 17, 2f),
        };

        /// <summary>Nivel 1 siempre. Los valores negativos se tratan como nivel 1.</summary>
        public static LevelDefinition For(int level)
        {
            int n = level < 1 ? 1 : level;

            if (n <= LastFixedLevel)
            {
                return Fixed[n - 1];
            }

            int size = Math.Min(MaxBoardSize, 5 + (n - 6) / 2);
            int cells = size * size;
            int minMines = Math.Max(2, (int)Math.Round(cells * 0.24));
            int maxMines = Math.Min(cells - 1, minMines + 2);

            return new LevelDefinition(n, size, minMines, maxMines, 2f);
        }
    }
}
