import { createApp } from 'vue';
import './styles/global.css';
import { loadAppConfig } from '@/shared/config/app-config';
import App from './App.vue';
import { installProviders } from './providers';

// app-config.json is fetched before anything reads the config (API base url, feature flags).
await loadAppConfig();

const app = createApp(App);
installProviders(app);
app.mount('#app');
