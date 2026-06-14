import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'ECode',
  description: 'Windows-native SuperTerminal documentation',
  cleanUrls: true,
  lastUpdated: true,
  themeConfig: {
    logo: '/app-icon.png',
    nav: [
      { text: 'Guide', link: '/getting-started' },
      { text: 'CLI', link: '/cli' },
      { text: 'API', link: '/browser-api' },
      { text: 'Roadmap', link: '/roadmap' }
    ],
    sidebar: [
      {
        text: 'Start',
        items: [
          { text: 'Overview', link: '/' },
          { text: 'Installation', link: '/installation' },
          { text: 'Getting Started', link: '/getting-started' },
          { text: 'Keyboard Shortcuts', link: '/keyboard-shortcuts' }
        ]
      },
      {
        text: 'Configure',
        items: [
          { text: 'Configuration', link: '/configuration' },
          { text: 'Custom Commands', link: '/custom-commands' },
          { text: 'Session Restore', link: '/session-restore' }
        ]
      },
      {
        text: 'Automate',
        items: [
          { text: 'Browser API', link: '/browser-api' },
          { text: 'CLI Reference', link: '/cli' }
        ]
      },
      {
        text: 'Operate',
        items: [
          { text: 'Troubleshooting', link: '/troubleshooting' },
          { text: 'Architecture', link: '/architecture' },
          { text: 'Roadmap', link: '/roadmap' }
        ]
      }
    ],
    search: {
      provider: 'local'
    },
    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright (c) ECode contributors'
    }
  }
})
