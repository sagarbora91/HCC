// Entry point. See app/App.tsx for the shell and services/mock for the prototype mock service.
import React from 'react';
import {createRoot} from 'react-dom/client';
import {App} from './app/App';
import {installA11yEnhancer} from './components/ui';
// Wave TRN training driver: prototype build with ?training=1 only; the define makes this branch (and the driver) vanish from the production bundle.
if(process.env.HCC_BUILD!=='production'&&new URLSearchParams(location.search).has('training'))void import('./app/demo/training');
createRoot(document.getElementById('root')!).render(<App/>);
installA11yEnhancer();
