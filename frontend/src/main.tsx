import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import App from "./App";
import "./index.css";

// tema y vista sin flash: se aplican antes del primer render
const theme = localStorage.getItem("rag-theme") ?? "dark";
const view = localStorage.getItem("rag-view") ?? "landing";
document.documentElement.dataset.theme = theme;
document.documentElement.dataset.view = view;

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
