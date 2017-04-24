// Dummy bundle.js that loads the actual bundle from the dev server and sets a flag on the window.
const el = document.createElement("script");
el.src = "https://localhost:8080/built/bundle.js";
document.head.appendChild(el);
window.ACE_DEV = true;