﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Rage;
using Rage.Native;

namespace DLSv2.Threads
{
    using Utils;


    internal class ControlsInput
    {
        private static Keys[] validKeyModifiers = new Keys[] { Keys.None, Keys.LShiftKey, Keys.RShiftKey, Keys.LControlKey, Keys.RControlKey, Keys.Alt };
        private static List<Keys> keyModifiersInUse = new List<Keys>();
        private static List<ControllerButtons> btnModifiersInUse = new List<ControllerButtons>();

        public string Name { get; }
        public Keys Key { get; set; }
        public Keys KeyModifier { get; set; }
        public Keys KeyModifier2 { get; set; }
        public ControllerButtons Button { get; set; }
        public ControllerButtons ButtonModifier { get; set; }

        public override string ToString()
        {
            string info = $"ControlsInput [{Name}]: ";
            if (KeyModifier != Keys.None) info += $"{KeyModifier} + ";
            if (KeyModifier2 != Keys.None) info += $"{KeyModifier2} + ";
            if (Key != Keys.None) info += $"{Key} ";

            if (Key != Keys.None && Button != ControllerButtons.None) info += "  /  ";

            if (ButtonModifier != ControllerButtons.None) info += $"{ButtonModifier} + ";
            if (Button != ControllerButtons.None) info += $"{Button}";

            return info;
        }

        // Event handler delegate for events sent by this input
        public delegate void InputEvent(ControlsInput sender, string inputName);
        // Invoked when this specific input is initially pressed
        public event InputEvent OnInputPressed;
        // Invoked when this specific input is released after having been pressed
        public event InputEvent OnInputReleased;
        // Invoked when any input is initially pressed
        public static event InputEvent OnAnyInputPressed;
        // Invoked when any input is released after having been pressed
        public static event InputEvent OnAnyInputReleased;

        // was the key combo held down on the last tick
        private bool wasHeld;

        public ControlsInput(string name) 
        {
            Name = name;

            Key = Settings.INI.ReadEnum(name, "Key", Keys.None);
            KeyModifier = Settings.INI.ReadEnum(name, "KeyModifier", Keys.None);
            KeyModifier2 = Settings.INI.ReadEnum(name, "KeyModifier2", Keys.None);

            Button = Settings.INI.ReadEnum(name, "Button", ControllerButtons.None);
            ButtonModifier = Settings.INI.ReadEnum(name, "ButtonModifier", ControllerButtons.None);
        }

        public bool Validate()
        {
            bool defined = (Key != Keys.None || Button != ControllerButtons.None);
            bool areKeyModsValid = validKeyModifiers.Contains(KeyModifier) && validKeyModifiers.Contains(KeyModifier2);

            if (!defined) ($"Input [{Name}]: Key or Button must be defined").ToLog();
            if (!areKeyModsValid) ($"Input [{Name}: Key modifier must be one of " + string.Join(", ", validKeyModifiers.Select(k => k.ToString()))).ToLog();

            return defined && areKeyModsValid;
        }

        public void Process()
        {
            bool isHeld = IsHeldDown;
            bool isPressed = IsJustPressed;

            if (!ControlsManager.KeysLocked || Name == "LOCKALL")
            {
                if (wasHeld && !isHeld)
                {
                    OnInputReleased?.Invoke(this, Name);
                    OnAnyInputReleased?.Invoke(this, Name);
                }

                if (isPressed && !wasHeld)
                {
                    OnInputPressed?.Invoke(this, Name);
                    OnAnyInputPressed?.Invoke(this, Name);
                }
            }

            wasHeld = isHeld;
        }

        private bool IsKeyAvailable()
        {
            return 
                Key != Keys.None &&
                !isTextboxOpen &&
                !isAnyOtherModifierKeyPressed() &&
                (KeyModifier == Keys.None || Game.IsKeyDownRightNow(KeyModifier)) &&
                (KeyModifier2 == Keys.None || Game.IsKeyDownRightNow(KeyModifier2));
        }

        private bool IsButtonAvailable()
        {
            return
                Button != ControllerButtons.None &&
                !isAnyOtherModifierButtonPressed() &&
                (ButtonModifier == ControllerButtons.None || Game.IsControllerButtonDownRightNow(ButtonModifier));

        }

        public bool IsJustPressed => IsKeyJustPressed() || IsButtonJustPressed();

        private bool IsKeyJustPressed() => IsKeyAvailable() && Game.IsKeyDown(Key);

        private bool IsButtonJustPressed() => IsButtonAvailable() && Game.IsControllerButtonDown(Button);

        public bool IsHeldDown => IsKeyHeldDown() || IsButtonHeldDown();

        private bool IsKeyHeldDown() => IsKeyAvailable() && Game.IsKeyDownRightNow(Key);

        private bool IsButtonHeldDown() => IsButtonAvailable() && Game.IsControllerButtonDownRightNow(Button);


        private bool isTextboxOpen => NativeFunction.Natives.UPDATE_ONSCREEN_KEYBOARD<int>() == 0;
        private bool isAnyOtherModifierKeyPressed()
        {
            // check all potential modifier keys
            // return true if any modifier key that is not used for this input is pressed 
            foreach (Keys modifier in validKeyModifiers)
            {
                if (modifier != Keys.None && modifier != KeyModifier && modifier != KeyModifier2 && Game.IsKeyDownRightNow(modifier)) return true;
            }
            return false;
        }

        private bool isAnyOtherModifierButtonPressed()
        {
            // check all buttons which are currently registered as modifiers for any input
            // return true if any potential modifier button that is not used for this input is pressed
            foreach (var btn in btnModifiersInUse)
            {
                if (btn != ControllerButtons.None && btn != ButtonModifier && btn != Button && Game.IsControllerButtonDownRightNow(btn)) return true;
            }
            return false;
        }
    }

    internal static class ControlsManager
    {
        public static Dictionary<string, ControlsInput> Inputs = new Dictionary<string, ControlsInput>();
        public static bool KeysLocked = false;

        public static bool RegisterInput(string inputName)
        {
            // input does not exist
            if (inputName == null) return false;

            // normalize the input name
            inputName = inputName.Trim().ToUpper();

            // input is not defined in the INI
            // TODO: auto-add a placeholder for it in the INI
            if (!Settings.INI.DoesSectionExist(inputName)) return false;

            // input was already registered
            if (Inputs.ContainsKey(inputName)) return true;

            // input is not registered but is defined in the INI
            // create and register a new input
            ControlsInput input = new ControlsInput(inputName);

            // ensure input is valid, if so, register it
            if (input.Validate())
            {
                $"Registered {input}".ToLog();
                Inputs.Add(inputName, input);
                return true;
            }

            return false;
        }

        public static void Process()
        {
            while(true)
            {
                GameFiber.Yield();

                if (Game.IsPaused) continue;

                foreach (ControlsInput input in Inputs.Values)
                {
                    input.Process();
                }
            }
        }

        public static void ClearInputs() => Inputs.Clear();
        public static void PlayInputSound() => NativeFunction.Natives.PLAY_SOUND_FRONTEND(-1, Settings.AUDIONAME, Settings.AUDIOREF, true);

        public static List<int> DisabledControls = new List<int>();
        public static void DisableControls() => DisabledControls.ForEach(c => NativeFunction.Natives.DISABLE_CONTROL_ACTION(0, c, true));
        static ControlsManager()
        {
            foreach (string control in Settings.DISABLEDCONTROLS.Split(',').Select(s => s.Trim()).ToList())
                DisabledControls.Add(control.ToInt32());
        }

#if DEBUG
        [Rage.Attributes.ConsoleCommand]
        public static void TestInputs()
        {
            "Registering test inputs".ToLog();
            foreach (var section in Settings.INI.GetSectionNames())
                RegisterInput(section);

            ControlsInput.OnAnyInputPressed += Test_OnAnyInputPressed;
            ControlsInput.OnAnyInputReleased += Test_OnAnyInputReleased;

            Process();
        }

        private static void Test_OnAnyInputReleased(ControlsInput sender, string inputName)
        {
            Game.DisplayNotification($"~c~{Game.GameTime}~w~\n{inputName} ~y~released~w~");
            $"Input {inputName} released".ToLog();
        }

        private static void Test_OnAnyInputPressed(ControlsInput sender, string inputName)
        {
            Game.DisplayNotification($"~c~{Game.GameTime}~w~\n{inputName} ~g~pressed~w~");
            $"Input {inputName} pressed".ToLog();
        }
#endif
    }
}
