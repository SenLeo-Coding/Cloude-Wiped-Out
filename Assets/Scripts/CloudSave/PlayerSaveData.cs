using System;

namespace CloudGame.Save
{
    /// <summary>Modo de juego. Cada modo mantiene su propio rating.</summary>
    public enum GameMode
    {
        Casual = 0,
        Ranked = 1
    }

    /// <summary>Resultado de una partida para un jugador concreto.</summary>
    public enum MatchOutcome
    {
        Win = 0,
        Loss = 1,
        Draw = 2
    }

    /// <summary>
    /// Historial y rating de un solo modo de juego. Casual y Ranked son instancias
    /// separadas: perder en Ranked nunca toca el rating de Casual.
    /// </summary>
    [Serializable]
    public class MatchRecord
    {
        public const int DefaultRating = 1000;

        public int played;
        public int wins;
        public int losses;
        public int draws;
        public int rating = DefaultRating;

        /// <summary>Solo informativo; no se serializa en Cloud Save.</summary>
        public int WinRate => played > 0 ? (int)Math.Round(wins * 100f / played) : 0;

        public void Record(MatchOutcome outcome, int ratingDelta = 0)
        {
            played++;
            switch (outcome)
            {
                case MatchOutcome.Win:
                    wins++;
                    break;
                case MatchOutcome.Loss:
                    losses++;
                    break;
                default:
                    draws++;
                    break;
            }

            rating = ClampRating(rating + ratingDelta);
        }

        public void EnsureValid()
        {
            played = ClampCount(played);
            wins = ClampCount(wins);
            losses = ClampCount(losses);
            draws = ClampCount(draws);
            rating = ClampRating(rating);
        }

        internal static int ClampCount(int value) => value < 0 ? 0 : value;

        internal static int ClampRating(int value) => value < 0 ? 0 : value;
    }

    /// <summary>Datos públicos del jugador, editables dentro del juego.</summary>
    [Serializable]
    public class PlayerProfile
    {
        public string displayName = string.Empty;
        public string avatarId = string.Empty;

        /// <summary>Ticks de UTC del momento en que se creó la partida guardada por primera vez.</summary>
        public long createdUtcTicks;

        /// <summary>Nivel más alto alcanzado en una partida guardada.</summary>
        public int highestLevelCompleted;

        public void EnsureValid()
        {
            displayName = displayName ?? string.Empty;
            avatarId = avatarId ?? string.Empty;
            highestLevelCompleted = MatchRecord.ClampCount(highestLevelCompleted);
            if (createdUtcTicks <= 0)
            {
                createdUtcTicks = DateTime.UtcNow.Ticks;
            }
        }
    }

    /// <summary>Progreso acumulado del jugador, separado por modo de juego.</summary>
    [Serializable]
    public class PlayerStats
    {
        public int levelsCompleted;
        public int minesRevealed;
        public int gamesLost;

        public MatchRecord casual = new MatchRecord();
        public MatchRecord ranked = new MatchRecord();

        public MatchRecord For(GameMode mode) => mode == GameMode.Ranked ? ranked : casual;

        public void EnsureValid()
        {
            levelsCompleted = MatchRecord.ClampCount(levelsCompleted);
            minesRevealed = MatchRecord.ClampCount(minesRevealed);
            gamesLost = MatchRecord.ClampCount(gamesLost);

            casual = casual ?? new MatchRecord();
            ranked = ranked ?? new MatchRecord();
            casual.EnsureValid();
            ranked.EnsureValid();
        }
    }

    /// <summary>
    /// Documento completo que se guarda en Cloud Save. Se serializa con
    /// <c>JsonUtility</c>, así que solo se guardan campos públicos y todas las clases
    /// deben estar marcadas con <see cref="SerializableAttribute"/>.
    /// </summary>
    [Serializable]
    public class PlayerSaveData
    {
        /// <summary>
        /// Se incrementa cuando cambie el formato del documento. <see cref="Migrate"/> convierte
        /// las partidas guardadas con versiones anteriores al formato actual.
        /// </summary>
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public PlayerProfile profile = new PlayerProfile();
        public PlayerStats stats = new PlayerStats();

        public MatchRecord For(GameMode mode) => stats.For(mode);

        /// <summary>Crea el documento de un jugador nuevo con los valores por defecto.</summary>
        public static PlayerSaveData CreateNew(string displayName = null)
        {
            var data = new PlayerSaveData
            {
                schemaVersion = CurrentSchemaVersion
            };
            data.EnsureValid();

            if (!string.IsNullOrEmpty(displayName))
            {
                data.profile.displayName = displayName;
            }

            return data;
        }

        /// <summary>
        /// Deja el documento en un estado utilizable. Se llama tras leerlo de la nube para
        /// reparar datos cortados, campos ausentes en versiones antiguas o valores corruptos.
        /// </summary>
        public void EnsureValid()
        {
            profile = profile ?? new PlayerProfile();
            stats = stats ?? new PlayerStats();
            profile.EnsureValid();
            stats.EnsureValid();

            if (schemaVersion <= 0)
            {
                schemaVersion = CurrentSchemaVersion;
            }
        }

        /// <summary>Convierte documentos de versiones anteriores al formato actual.</summary>
        public void Migrate()
        {
            if (schemaVersion >= CurrentSchemaVersion)
            {
                return;
            }

            // Puntos de extensión: cada formato nuevo sube CurrentSchemaVersion y aplica aquí
            // los ajustes necesarios sobre documentos ya guardados en la nube.
            schemaVersion = CurrentSchemaVersion;
            EnsureValid();
        }
    }
}
