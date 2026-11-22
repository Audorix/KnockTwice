/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Optional absolute API origin. Leave unset for same-origin (Vite proxy or nginx). */
  readonly VITE_API_BASE_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
