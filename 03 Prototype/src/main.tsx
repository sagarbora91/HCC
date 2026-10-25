// Entry point. See app/App.tsx for the shell and services/mock for the prototype mock service.
import React from 'react';
import {createRoot} from 'react-dom/client';
import {App} from './app/App';
import {installA11yEnhancer} from './components/ui';
createRoot(document.getElementById('root')!).render(<App/>);
installA11yEnhancer();
