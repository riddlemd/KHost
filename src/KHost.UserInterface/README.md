## Development

### CSS/SCSS

1. Make changes to files in `wwwroot/scss/`
2. Run `npm run sass:watch` to automatically compile on save
3. Browser will refresh when CSS changes

#### Before Deployment
1. Run `npm run sass` to ensure all files are compiled
2. The compiled `app.css` will be generated in `wwwroot/css/`

#### Adding New Styles
1. Create a new SCSS partial file (e.g., `_my-component.scss`) in `wwwroot/scss/`
2. Add your styles using SCSS features:
   - Nesting
   - Variables (use CSS custom properties from themes like `var(--kh-purple)`)
   - Mixins (define if you have repeated patterns)
3. Import it in `wwwroot/scss/main.scss`
4. Recompile with `npm run sass`
5. If running `npm run sass:watch`, the changes will compile automatically

#### Troubleshooting

**Changes aren't appearing?**
- Make sure `npm run sass:watch` is running
- Check the browser cache (hard refresh: Ctrl+Shift+R)
- Look for compilation errors in the terminal

**npm commands not found?**
- Run `npm install` in the project directory
- Make sure you have Node.js installed

**Too many deprecation warnings?**
- These are from Sass about @import (will be @use in Dart Sass 3.0)
- They don't affect functionality
- Can be addressed in future refactoring
