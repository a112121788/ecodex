import process from 'node:process'
import { defineConfig } from 'vitepress'

const rawBase = process.env.VITEPRESS_BASE || '/'
const base = rawBase === '/' ? '/' : `/${rawBase.replace(/^\/|\/$/g, '')}/`

export default defineConfig({
  base,
  title: 'ECodex',
  description: 'Windows 原生 SuperTerminal 文档',
  cleanUrls: true,
  lastUpdated: true,
  themeConfig: {
    logo: '/app-icon.png',
    search: {
      provider: 'local'
    },
    nav: [
      { text: '指南', link: '/getting-started' },
      { text: '安装', link: '/installation' },
      { text: '命令行', link: '/cli' },
      { text: '浏览器 API', link: '/browser-api' },
      { text: '路线图', link: '/roadmap' }
    ],
    sidebar: [
      {
        text: '开始',
        items: [
          { text: '概览', link: '/' },
          { text: '安装', link: '/installation' },
          { text: '快速上手', link: '/getting-started' },
          { text: '快捷键', link: '/keyboard-shortcuts' }
        ]
      },
      {
        text: '配置',
        items: [
          { text: '配置说明', link: '/configuration' },
          { text: '自定义命令', link: '/custom-commands' },
          { text: '会话恢复', link: '/session-restore' }
        ]
      },
      {
        text: '自动化',
        items: [
          { text: '浏览器 API', link: '/browser-api' },
          { text: '命令行', link: '/cli' }
        ]
      },
      {
        text: '运维',
        items: [
          { text: '故障排查', link: '/troubleshooting' },
          { text: '发布就绪', link: '/release-readiness' },
          { text: '发布说明', link: '/release-notes/1.0.0' },
          { text: '架构概览', link: '/architecture' },
          { text: '路线图', link: '/roadmap' }
        ]
      }
    ],
    socialLinks: [
      { icon: 'github', link: 'https://github.com/a112121788/ecodex' }
    ],
    footer: {
      message: '基于 MIT License 发布。',
      copyright: 'Copyright (c) ECodex contributors'
    }
  }
})
