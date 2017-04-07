# Ace-Windows

## Launcher
Executable that launches the LCU and updates Ace.

## Injector
Executable that runs as a debugger to the LCU Ux process and injects Ace javascript

## Payload
Hooks code in libcef.dll to inject Ace on page loads.

# Building
Copy resources required by the Launcher to Launcher/Resources:
- `bundle.js`: The packaged Ace JavaScript/HTML/CSS

Build the solution in the root of this repository.

# Running
Use the Launcher.

Adding the flag --ace-dev when you run the launcher will inject `bundle_dev.js` instead of `bundle.js` (`bundle_dev.js` loads Ace from https://localhost:8080/built/bundle.js) and disables https cert checking to use the webpack development server.
