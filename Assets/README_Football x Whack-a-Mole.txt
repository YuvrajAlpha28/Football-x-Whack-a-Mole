MOLE SHOT – GAME PROJECT README

ABOUT THE GAME Mole Shot is a small portrait-mode mobile game made in
Unity. The player swipes to shoot a football and tries to hit moles
before they hide.

HOW TO PLAY 1. Press Start to begin the 60-second game. 2. Drag on the
screen to aim and adjust the shot. 3. Check the trajectory preview and
release to shoot. 4. Hit a mole while it is visible to earn points. 5.
Normal moles give 1 point and fast moles give 2 points. 6. Try to get
the highest score before the timer ends.

HOW IT WAS MADE The game was developed using Unity and C#. Rigidbody
physics is used to move the football with gravity-based projectile
motion. Swipe input is converted into a launch direction and velocity. A
Line Renderer displays the predicted ball trajectory, with a landing
marker to help the player aim.

The mole system uses six holes. Moles appear for a limited time, and the
player must hit them before they disappear. The game also includes score
feedback, a streak system, a timer, a start screen and an end screen.
Difficulty increases during the session by changing mole visibility and
fast-mole chances.

MAIN SCRIPTS - FootballController: Handles swipe input, aiming,
shooting, ball physics and respawning. - TrajectorySystem: Displays the
predicted trajectory and landing marker. - MoleHole: Controls each
mole’s visibility and hit detection. - MoleGameManager: Spawns moles and
handles hit and miss events. - MoleScoreUI: Updates the score and
gameplay messages. - GameSessionManager: Controls the start, timer, end
screen and restart.

PROJECT NOTE Gameplay values such as swipe sensitivity, launch power,
mole spawn timing and visibility duration can be adjusted in the Unity
Inspector.

Developed as a gameplay prototype using Unity and C#.
