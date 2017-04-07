# Ace-Windows

## Launcher
Executable that launches the LCU with Ace loaded and ready to go.

## Payload
Hooks code in libcef.dll to inject Ace on page loads.

# Building
Copy resources required by the Launcher to Launcher/Resources:
- `bundle.js`: The packaged Ace JavaScript/HTML/CSS

Build the solution in the root of this repository.

# Running
Use the Launcher.

Adding the flag --ace-dev when you run the launcher will inject `bundle_dev.js` instead of `bundle.js` (`bundle_dev.js` loads Ace from https://localhost:8080/built/bundle.js) and disables https cert checking to use the webpack development server.