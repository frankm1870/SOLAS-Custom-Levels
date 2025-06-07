# .level Structure
The general format of a line is `roomx, roomy, x, y = tilename params` or `roomx, roomy, x, y = tileid` to set a tile id directly.
Valid tilenames and parameters:
* `mirror <dir:direction> <moveable:bool> <rotateable:bool> <isBeatMirror:bool>`, dir is up for positive mirrors (like /) and anything else for negative mirrors (like \\); isBeatMirror determines if the mirror rotates on pulse hit.
* `prism <dir:direction> <moveable:bool> <rotateable:bool>`, dir is up for diagonal prisms and anything else for horizontal prisms.
* `emitter <dir:direction> <color:color|"void"> <moveable:bool> <spacing:int>`, color can be any color in the game, or void for an X sigil.
* `receiver <dir:direction> <color:color|"any"> <spacing:int>`, color can be any color in the game, or any for a wildcard receiver.
* `noplace`, a tile that blocks other tiles from being moved onto it.
* `outofbounds`, similar to a wall, but has no border.
* `wall`
* `default`, places whatever tile that would normally be at this position on a new save, useless if only modifying on new file.
* `glitch`
* `fakewall`, a tile that appears to be a wall but acts as a `noplace` tile.
* `glitchdestroyer`, a tile that is able to destroy up to 3 nearby glitches when pulses hit it.
* `filter <color:color>`, color can be any color in the game.
* `button <type:int>`, type determines what "channel" the button is on. The button will only toggle doors on the same channel as itself.
* `door <type:int> <state:"open"|"closed">`, state determines whether the door is open or closed, and type determines the channel of the door.
* `corner`, acts like a wall, but visually changes to connect to nearby walls or corner tiles.
* `blocker`, acts as a moveable wall.
* `teleporter <channel:int> <moveable:bool>`, teleports pulses to another teleporter with the same channel. There should only be two teleporters on each channel.
* `powernode <id:int>`, one of the large ringed tiles that destroys all glitches in its room. The parameter determines what the number of the powernode is. Upon being triggered all doors of the same id will be opened.
* `combolock <id:int> <position:int>`, creates a combination lock tile like the ones at the end of the secret area from the green branch. The id parameter should refer to a combolock group defined earlier in the file (more information below), and the position parameter is what "digit" in the code this tile is.
* `empty`
* `text <text:string> <font:string> <size:int>`, creates a piece of text with the specified text, font, and font size.

Types `color` and `direction` are enumerations with the following values:
* For `color`:
  * `red`
  * `green`
  * `yellow`
  * `blue`
  * `magenta`
  * `cyan`
  * `white`
* For `direction`:
  * `up`
  * `right`
  * `down`
  * `left`

Parameters should only be specified with their value and nothing else.
For combolock groups, the syntax is `*combolock groupid code glitchPositions`, the code should be specified by converting the colors to numbers (red = 1, green = 2, yellow = 3, blue = 4, magenta = 5, cyan = 6, white = 7, X/empty = 0). The glitchPositions parameter should be a space separated list of the positions of the glitches to be destroyed when the code is correctly entered.
(E.g. `*combolock 1 163 32,232 33,232`)
# .zone Structure
The format of a line is `x, y = zone`, where x and y are the room coordinates and zone is one of the below:
* `intro`
* `hub`
* `red`
* `green`
* `blue`
* `secret`
* `boss`
---
For either file an octothorpe (#) can be used to mark a comment.
