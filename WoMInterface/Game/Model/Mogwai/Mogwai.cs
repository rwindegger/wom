﻿using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using WoMInterface.Game.Enums;
using WoMInterface.Game.Interaction;
using WoMInterface.Tool;

namespace WoMInterface.Game.Model
{
    public class Mogwai : Entity
    {
        private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly int blockHeight;


        private List<Shift> Shifts { get; }

        public MogwaiState MogwaiState { get; set; }

        public Dictionary<int, Shift> LevelShifts { get; }

        public int Pointer { get; private set; }

        public string Key { get; }

        public Coat Coat { get; }

        public Body Body { get; }

        public Stats Stats { get; }

        public Experience Experience { get; set; }

        public double Exp { get; private set; } = 0;

        public int CurrentLevel { get; private set; } = 1;

        public double XpToLevelUp => CurrentLevel * 1000;

        public Mogwai(string key, List<Shift> shifts)
        {
            Key = key;
            Shifts = shifts;
            LevelShifts = new Dictionary<int, Shift>();

            var creationShift = shifts[0];

            blockHeight = creationShift.Height;
            Pointer = creationShift.Height;

            // create appearance           
            var hexValue = new HexValue(creationShift);
            Name = NameGen.GenerateName(hexValue);
            Body = new Body(hexValue);
            Coat = new Coat(hexValue);
            Stats = new Stats(hexValue);

            // create abilities
            int[] rollEvent = new int[] {4,6,3};
            Gender = creationShift.Dice.Roll(2, -1);
            Strength = creationShift.Dice.Roll(rollEvent);
            Dexterity = creationShift.Dice.Roll(rollEvent);
            Constitution = creationShift.Dice.Roll(rollEvent);
            Inteligence = creationShift.Dice.Roll(rollEvent);
            Wisdom = creationShift.Dice.Roll(rollEvent);
            Charisma = creationShift.Dice.Roll(rollEvent);

            NaturalArmor = 0;
            SizeType = SizeType.MEDIUM;

            BaseAttackBonus = 1;

            // create experience
            Experience = new Experience(creationShift);

            // add simple hand as weapon
            Equipment.BaseWeapon = new Fist();

            HitPointDice = 8;
            CurrentHitPoints = MaxHitPoints;

        }

        public void Evolve(int blockHeight = 0)
        {
            int oldPointer = Pointer;

            foreach(var shift in Shifts)
            {
                // only evolve to the target block height
                if (blockHeight != 0 && shift.Height > blockHeight)
                {
                    break;
                }

                // only proccess shifts that aren't proccessed before ...
                if (shift.Height <= Pointer)
                {
                    continue;
                }

                // setting pointer to the actual shift
                Pointer = shift.Height;

                // first we always calculated current lazy experience
                double lazyExp = Experience.GetExp(CurrentLevel, shift);
                if (lazyExp > 0)
                {
                    AddExp(Experience.GetExp(CurrentLevel, shift), shift);
                }

                // lazy health regeneration
                if (MogwaiState == MogwaiState.NONE)
                {
                    int naturalHealing = shift.IsSmallShift ? 2 * CurrentLevel : CurrentLevel;
                    CurrentHitPoints += naturalHealing;
                    if (CurrentHitPoints > MaxHitPoints)
                    {
                        CurrentHitPoints = MaxHitPoints;
                    }
                }
            }

            CommandLine.InGameMessage($"Evolved {Name} from ");
            CommandLine.InGameMessage($"{oldPointer}", ConsoleColor.Green);
            CommandLine.InGameMessage($" to ");
            CommandLine.InGameMessage($"{Pointer}", ConsoleColor.Green);
            CommandLine.InGameMessage($"!", true);
        }

        public void AddExp(double exp, Shift shift)
        {
            CommandLine.InGameMessage($"You just earned ");
            CommandLine.InGameMessage($"+{exp}", ConsoleColor.Green);
            CommandLine.InGameMessage($" experience!", true);

            Exp += exp;

            if (Exp >= XpToLevelUp)
            {
                CurrentLevel += 1;
                LevelShifts.Add(CurrentLevel, shift);
                LevelUp(shift);
            }
        }

        /// <summary>
        /// Passive level up, includes for example hit point roles.
        /// </summary>
        /// <param name="shift"></param>
        private void LevelUp(Shift shift)
        {
            CommandLine.InGameMessage($"You're mogwai suddenly feels an ancient power around him.", ConsoleColor.Yellow, true);
            CommandLine.InGameMessage($"Congratulations he just made the ", ConsoleColor.Yellow);
            CommandLine.InGameMessage($"{CurrentLevel}", ConsoleColor.Green);
            CommandLine.InGameMessage($" th level!", ConsoleColor.Yellow, true);

            // hit points roll
            HitPointLevelRolls.Add(shift.Dice.Roll(HitPointDice));
            
            // leveling up will heal you to max hitpoints
            CurrentHitPoints = MaxHitPoints;
        }

        public void Print()
        {
            Shift shift = Shifts[0];

            Console.WriteLine("*** Mogwai Nascency Transaction ***");
            Console.WriteLine($"- Time: {shift.Time}");
            Console.WriteLine($"- Index: {shift.BkIndex}");
            Console.WriteLine($"- Amount: {shift.Amount}");
            Console.WriteLine($"- Height: {shift.Height}");
            Console.WriteLine($"- AdHex: {shift.AdHex}");
            Console.WriteLine($"- BlHex: {shift.BkHex}");
            Console.WriteLine($"- TxHex: {shift.TxHex}");

            Console.WriteLine();
            Console.WriteLine("*** Mogwai Attributes ***");
            Console.WriteLine("- Body:");
            Body.All.ForEach(p => Console.WriteLine($"{p.Name}: {p.GetValue()} [{p.MinRange}-{p.Creation-1}] Var:{p.MaxRange}-->{p.Valid}"));
            Console.WriteLine("- Coat:");
            Coat.All.ForEach(p => Console.WriteLine($"{p.Name}: {p.GetValue()} [{p.MinRange}-{p.Creation-1}] Var:{p.MaxRange}-->{p.Valid}"));
            Console.WriteLine("- Stats:");
            Stats.All.ForEach(p => Console.WriteLine($"{p.Name}: {p.GetValue()} [{p.MinRange}-{p.Creation-1}] Var:{p.MaxRange}-->{p.Valid}"));
            Experience.Print();
        }
    }
}