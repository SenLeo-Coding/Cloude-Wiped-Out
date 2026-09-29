using System.Collections.Generic;
using System.Linq;
using CloudGame.Gameplay;
using NUnit.Framework;

namespace CloudGame.Tests.EditMode
{
    public class MatchEngineTests
    {
        [Test]
        public void EmpiezaEnElNivelUnoConTresVidasCadaUno()
        {
            var match = new MatchEngine(seed: 1);

            Assert.That(match.Level, Is.EqualTo(1));
            Assert.That(match.PlayerOneLives, Is.EqualTo(MatchEngine.MaxLives));
            Assert.That(match.PlayerTwoLives, Is.EqualTo(MatchEngine.MaxLives));
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.InProgress));
        }

        [Test]
        public void LosNivelesImparesSonDelJugadorUnoYLosParesDelDos()
        {
            Assert.That(MatchEngine.LevelOwnerFor(1), Is.EqualTo(Player.PlayerOne));
            Assert.That(MatchEngine.LevelOwnerFor(2), Is.EqualTo(Player.PlayerTwo));
            Assert.That(MatchEngine.LevelOwnerFor(3), Is.EqualTo(Player.PlayerOne));
            Assert.That(MatchEngine.LevelOwnerFor(4), Is.EqualTo(Player.PlayerTwo));
        }

        [Test]
        public void EmpiezaElResponsableDelNivelUno()
        {
            var match = new MatchEngine(seed: 2);

            Assert.That(match.CurrentPlayer, Is.EqualTo(Player.PlayerOne));
            Assert.That(match.IsCurrentPlayerLevelOwner, Is.True);
        }

        [Test]
        public void RevelarUnaCasillaSeguraPasaElTurnoSinRestarVidas()
        {
            var match = new MatchEngine(seed: 3);

            var result = match.Reveal(FirstUnrevealed(match, mine: false));

            Assert.That(result.Outcome, Is.EqualTo(RevealOutcome.Safe));
            Assert.That(result.WhoPlayed, Is.EqualTo(Player.PlayerOne));
            Assert.That(result.NextPlayer, Is.EqualTo(Player.PlayerTwo));
            Assert.That(result.LivesLost, Is.EqualTo(0));
            Assert.That(match.PlayerOneLives, Is.EqualTo(MatchEngine.MaxLives));
        }

        [Test]
        public void RevelarUnaMinaPasaElTurnoYRestaUnaVida()
        {
            var match = new MatchEngine(seed: 4);

            var result = match.Reveal(FirstUnrevealed(match, mine: true));

            Assert.That(result.Outcome, Is.EqualTo(RevealOutcome.Mine));
            Assert.That(result.NextPlayer, Is.EqualTo(Player.PlayerTwo), "el turno pasa también al pulsar una mina");
            Assert.That(result.LivesLost, Is.EqualTo(1));
            Assert.That(match.PlayerOneLives, Is.EqualTo(MatchEngine.MaxLives - 1));
        }

        [Test]
        public void ElTurnoAlternaEntreJugadores()
        {
            var match = new MatchEngine(seed: 5);
            var orden = new List<Player>();

            for (int i = 0; i < 4; i++)
            {
                orden.Add(match.CurrentPlayer);

                int cell = FirstUnrevealed(match, mine: true);
                if (cell < 0)
                {
                    cell = FirstUnrevealed(match, mine: false);
                }

                match.Reveal(cell);
            }

            Assert.That(orden, Is.EqualTo(new[]
            {
                Player.PlayerOne,
                Player.PlayerTwo,
                Player.PlayerOne,
                Player.PlayerTwo,
            }));
            Assert.That(match.CurrentPlayer, Is.EqualTo(Player.PlayerOne), "tras cuatro jugadas le toca al primero");
        }

        [Test]
        public void RevelarUnaCasillaYaReveladaNoCambiaElTurno()
        {
            var match = new MatchEngine(seed: 6);
            int segura = FirstUnrevealed(match, mine: false);
            match.Reveal(segura);
            Player trasRevelar = match.CurrentPlayer;

            var result = match.Reveal(segura);

            Assert.That(result.Outcome, Is.EqualTo(RevealOutcome.AlreadyRevealed));
            Assert.That(match.CurrentPlayer, Is.EqualTo(trasRevelar));
        }

        [Test]
        public void QuedarseConCasillasOcultasAlAvanzarCuestaUnaVida()
        {
            var match = new MatchEngine(seed: 7);
            Assert.That(match.Board.RemainingSafeCells, Is.GreaterThan(0));

            var result = match.AdvanceLevel();

            Assert.That(result.Outcome, Is.EqualTo(AdvanceOutcome.Advanced));
            Assert.That(result.SkippedCellsPenalty, Is.True);
            Assert.That(result.LivesLost, Is.EqualTo(1));
            Assert.That(match.PlayerOneLives, Is.EqualTo(MatchEngine.MaxLives - 1));
            Assert.That(match.Level, Is.EqualTo(2));
        }

        [Test]
        public void AvanzarSinCasillasOcultasNoCuestaVida()
        {
            var match = new MatchEngine(seed: 8);
            ClearBoard(match);

            var result = match.AdvanceLevel();

            Assert.That(result.SkippedCellsPenalty, Is.False);
            Assert.That(result.LivesLost, Is.EqualTo(0));
            Assert.That(match.PlayerOneLives, Is.EqualTo(MatchEngine.MaxLives));
        }

        [Test]
        public void SoloElResponsableDelNivelPuedeAvanzar()
        {
            var match = new MatchEngine(seed: 9);
            match.Reveal(FirstUnrevealed(match, mine: false));

            Assert.That(match.CurrentPlayer, Is.EqualTo(Player.PlayerTwo), "tras revelar le toca al Jugador 2");
            Assert.That(match.IsCurrentPlayerLevelOwner, Is.False, "pero el nivel 1 es del Jugador 1");

            var result = match.AdvanceLevel();

            Assert.That(result.Outcome, Is.EqualTo(AdvanceOutcome.NotLevelOwner));
            Assert.That(match.Level, Is.EqualTo(1), "el nivel no cambia");
        }

        [Test]
        public void AlAvanzarElTurnoPasaAlResponsableDelSiguienteNivel()
        {
            var match = new MatchEngine(seed: 10);
            match.AdvanceLevel();

            Assert.That(match.Level, Is.EqualTo(2));
            Assert.That(match.LevelOwner, Is.EqualTo(Player.PlayerTwo));
            Assert.That(match.CurrentPlayer, Is.EqualTo(Player.PlayerTwo));
        }

        [Test]
        public void ElTableroLimpioPermiteAvanzarAlRivalParaNoBloquear()
        {
            var match = new MatchEngine(seed: 31);

            // Una mina para que el turno pase al Jugador 2, que no es el
            // responsable del nivel 1.
            match.Reveal(FirstUnrevealed(match, mine: true));
            Assert.That(match.CurrentPlayer, Is.EqualTo(Player.PlayerTwo));
            Assert.That(match.IsCurrentPlayerLevelOwner, Is.False, "el rival no es responsable del nivel 1");

            ClearBoard(match);
            Assert.That(match.Board.IsCleared, Is.True, "no queda nada que revelar");
            Assert.That(match.CurrentPlayer, Is.EqualTo(Player.PlayerTwo), "sigue sin poder jugar");

            // Aun así debe poder avanzar, o la partida quedaría bloqueada.
            var result = match.AdvanceLevel();

            Assert.That(result.Outcome, Is.EqualTo(AdvanceOutcome.Advanced));
            Assert.That(result.Level, Is.EqualTo(2));
            Assert.That(result.SkippedCellsPenalty, Is.False, "no queda nada oculto, no penaliza");
            Assert.That(match.PlayerTwoLives, Is.EqualTo(MatchEngine.MaxLives), "no pierde vida al avanzar limpio");
        }

        [Test]
        public void QuedarseSinVidasTerminaLaPartidaDeInmediato()
        {
            var match = new MatchEngine(seed: 11);

            DriveToGameOver(match);

            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Finished), "al agotarse las vidas la partida acaba");

            int perdidas = match.PlayerOneLives <= 0 ? match.PlayerOneLives : match.PlayerTwoLives;
            int supervivientes = match.PlayerOneLives <= 0 ? match.PlayerTwoLives : match.PlayerOneLives;
            Assert.That(perdidas, Is.LessThanOrEqualTo(0), "alguien se queda a 0 vidas");
            Assert.That(supervivientes, Is.GreaterThan(0), "el otro conserva vidas");
            Assert.That(match.Winner, Is.EqualTo(match.PlayerOneLives > 0 ? Player.PlayerOne : Player.PlayerTwo));
        }

        [Test]
        public void CaerEnMinasTerminaLaPartidaEnVariosNiveles()
        {
            // El nivel 1 es 2x2 con 2 minas, así que no deja ver caer 3 vidas en un
            // solo nivel: hay que avanzar para agotar la partida.
            var match = new MatchEngine(seed: 21);

            DriveToGameOver(match);

            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(match.Level, Is.GreaterThan(1), "hace falta pasar de nivel para agotar las vidas");
        }

        [Test]
        public void LaPartidaTerminadaIgnoraNuevasAcciones()
        {
            var match = new MatchEngine(seed: 12);
            DriveToGameOver(match);
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Finished), "precondición: la partida terminó");

            var reveal = match.Reveal(0);
            var advance = match.AdvanceLevel();

            Assert.That(reveal.Outcome, Is.EqualTo(RevealOutcome.MatchFinished));
            Assert.That(advance.Outcome, Is.EqualTo(AdvanceOutcome.MatchFinished));
        }

        [Test]
        public void PerderLaUltimaVidaAvanzandoTerminaLaPartida()
        {
            var match = new MatchEngine(seed: 13);

            // Presionamos minas al Jugador 1 y casillas seguras al Jugador 2
            // hasta que al Jugador 1 le quede una sola vida, le toque turno y
            // sea responsable de un nivel con casillas seguras aún ocultas.
            int guard = 0;
            while (guard++ < 500 && match.Phase == MatchPhase.InProgress)
            {
                if (match.PlayerOneLives == 1 &&
                    match.CurrentPlayer == Player.PlayerOne &&
                    match.IsCurrentPlayerLevelOwner &&
                    match.Board.RemainingSafeCells > 0)
                {
                    break;
                }

                // Al Jugador 1 solo le quitamos vidas con minas mientras tenga
                // más de una, para no acabar antes de tiempo.
                if (match.CurrentPlayer == Player.PlayerOne && match.PlayerOneLives > 1)
                {
                    int mine = FirstUnrevealed(match, mine: true);
                    if (mine >= 0)
                    {
                        match.Reveal(mine);
                        continue;
                    }
                }

                int safe = FirstUnrevealed(match, mine: false);
                if (safe >= 0)
                {
                    match.Reveal(safe);
                    continue;
                }

                if (match.AdvanceLevel().Outcome != AdvanceOutcome.Advanced)
                {
                    break;
                }
            }

            Assert.That(match.PlayerOneLives, Is.EqualTo(1), "el Jugador 1 debe quedarse con una vida");
            Assert.That(match.CurrentPlayer, Is.EqualTo(Player.PlayerOne));
            Assert.That(match.IsCurrentPlayerLevelOwner, Is.True, "le toca al responsable del nivel");
            Assert.That(match.Board.RemainingSafeCells, Is.GreaterThan(0), "quedan casillas seguras ocultas");

            var result = match.AdvanceLevel();

            Assert.That(result.SkippedCellsPenalty, Is.True, "avanzar con casillas ocultas penaliza");
            Assert.That(result.LivesLost, Is.EqualTo(1));
            Assert.That(match.PlayerOneLives, Is.EqualTo(0), "pierde su última vida");
            Assert.That(result.MatchEnded, Is.True, "la partida termina de inmediato");
            Assert.That(match.Phase, Is.EqualTo(MatchPhase.Finished));
            Assert.That(match.Winner, Is.EqualTo(Player.PlayerTwo));
        }

        [Test]
        public void CadaNivelConstruyeUnTableroNuevo()
        {
            var match = new MatchEngine(seed: 14);

            Assert.That(match.Board.Size, Is.EqualTo(2), "el nivel 1 es 2x2");
            Assert.That(match.Board.RevealedCount, Is.EqualTo(0), "el tablero arranca sin revelar");

            match.AdvanceLevel();

            Assert.That(match.Level, Is.EqualTo(2));
            Assert.That(match.Board.Size, Is.EqualTo(3), "el nivel 2 es 3x3");
            Assert.That(match.Board.RevealedCount, Is.EqualTo(0));
        }

        [Test]
        public void ElTableroSeLimpiaAntesDeAvanzar()
        {
            var match = new MatchEngine(seed: 15);
            ClearBoard(match);

            Assert.That(match.Board.IsCleared, Is.True);

            var result = match.AdvanceLevel();

            Assert.That(result.SkippedCellsPenalty, Is.False);
            Assert.That(match.Level, Is.EqualTo(2));
        }

        private static int FirstUnrevealed(MatchEngine match, bool mine)
        {
            var board = match.Board;
            for (int i = 0; i < board.CellCount; i++)
            {
                if (!board.IsRevealed(i) && board.IsMine(i) == mine)
                {
                    return i;
                }
            }

            return -1;
        }

        private static void ClearBoard(MatchEngine match)
        {
            var board = match.Board;
            for (int i = 0; i < board.CellCount; i++)
            {
                if (!board.IsMine(i))
                {
                    board.Reveal(i);
                }
            }
        }

        /// <summary>
        /// Lleva la partida hasta el final pulsando minas siempre que queden, y
        /// avanzando de nivel cuando el tablero se queda sin minas sin revelar.
        /// Así se agotan las vidas sin depender de la semilla concreta.
        /// </summary>
        private static void DriveToGameOver(MatchEngine match, int maxSteps = 500)
        {
            int steps = 0;
            while (match.Phase == MatchPhase.InProgress && steps++ < maxSteps)
            {
                int mine = FirstUnrevealed(match, mine: true);
                if (mine >= 0)
                {
                    match.Reveal(mine);
                    continue;
                }

                int safe = FirstUnrevealed(match, mine: false);
                if (safe >= 0)
                {
                    match.Reveal(safe);
                    continue;
                }

                match.AdvanceLevel();
            }
        }
    }
}
